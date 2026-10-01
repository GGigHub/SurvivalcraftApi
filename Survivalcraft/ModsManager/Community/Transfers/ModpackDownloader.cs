namespace Game {
    sealed class ModpackDownloader {
        // 下载阶段的有界并发预取（原逐项串行下载 20 子项 20-60s → 约 1/3）；导入仍逐项
        // 保序执行（进度展示与失败汇总依赖清单顺序）。批大小即内存峰值（批内数据驻留），
        // Android 内存受限下不做整批全量预取。
        const int PrefetchBatchSize = 4;

        readonly IModpackSourceResolver m_sourceResolver;
        readonly IModpackItemImporter m_itemImporter;
        readonly Func<int, bool> m_isCurrentRequest;

        public ModpackDownloader(IModpackSourceResolver sourceResolver, IModpackItemImporter itemImporter, Func<int, bool> isCurrentRequest) {
            m_sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
            m_itemImporter = itemImporter ?? throw new ArgumentNullException(nameof(itemImporter));
            m_isCurrentRequest = isCurrentRequest ?? throw new ArgumentNullException(nameof(isCurrentRequest));
        }

        /// <summary>单条目的并发预取结果；ValidationFailed 标记校验失败（不发起下载），ResolveError 存解析异常。</summary>
        sealed class PrefetchedItem {
            public bool ValidationFailed;
            public byte[] Data;
            public string DownloadedFileName;
            public string ResolvedUrl;
            public PostFileItem CommunityFile;
            public string ResolveError;
        }

        public async Task<ModpackDownloadProgress> ExecuteAsync(int requestId,
            IEnumerable<ModpackItem> items,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            string apiBaseUrl,
            string token,
            Func<ModpackItem, byte[], ModpackDownloadProgress, byte[]> prepareCustomDataFile,
            Func<ModpackItem, string> noDownloadLinkError,
            Func<ModpackItem, string> unknownItemCategoryError,
            CancellationToken cancellationToken = default,
            Func<ModpackItem, string> hostUnavailableError = null,
            Func<ExternalContentType, string, bool> hasExactLocalContent = null) {
            if (noDownloadLinkError == null) {
                throw new ArgumentNullException(nameof(noDownloadLinkError));
            }
            if (unknownItemCategoryError == null) {
                throw new ArgumentNullException(nameof(unknownItemCategoryError));
            }
            IReadOnlyList<ModpackItem> selectedItems = [.. (items ?? []).Where(item => item != null && item.IsEnabled)];
            ModpackDownloadProgress progress = new() { TotalCount = selectedItems.Count, Results = [] };
            for (int offset = 0; offset < selectedItems.Count; offset += PrefetchBatchSize) {
                if (!m_isCurrentRequest(requestId)) {
                    // 请求已被取代/取消：与 ResolveAsync 后复查的 break 一致标记中断，
                    // 调用方据此识别「被中断」而不是展示误导性的完成统计。
                    progress.Interrupted = true;
                    break;
                }
                int count = Math.Min(PrefetchBatchSize, selectedItems.Count - offset);
                IReadOnlyList<ModpackItem> batch = [.. selectedItems.Skip(offset).Take(count)];
                PrefetchedItem[] fetched = await PrefetchBatchAsync(batch, embeddedFiles, apiBaseUrl, token, cancellationToken);
                for (int i = 0; i < batch.Count; i++) {
                    ModpackItem item = batch[i];
                    item.Filename = ModpackRules.GetItemFileName(item);
                    ModpackItemDownloadResult itemResult = new() { Item = item };
                    try {
                        if (!ImportFetchedItem(
                                requestId,
                                item,
                                fetched[i],
                                progress,
                                itemResult,
                                embeddedFiles,
                                prepareCustomDataFile,
                                noDownloadLinkError,
                                unknownItemCategoryError,
                                cancellationToken,
                                hasExactLocalContent,
                                hostUnavailableError
                            )) {
                            // 导入前复查发现请求被取代：终止整批（含余下批次）。
                            progress.Interrupted = true;
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        // 用户取消：传播中止整批；超时 OCE 不满足 when，仍按逐项失败记录。
                        throw;
                    }
                    catch (Exception ex) {
                        // 单项阶段（跳过判定/清单文件信息应用/文件名推导等）异常降级为该单项失败，
                        // 整包继续——与旧版逐项 try 语义一致，不升级为整包顶层 Error。
                        itemResult.Error = ex.Message;
                        progress.FailedCount++;
                    }
                    progress.Results.Add(itemResult);
                    // CompletedCount 与 Results 配对：单项已有结果才计入。
                    progress.CompletedCount++;
                }
                if (progress.Interrupted) {
                    break;
                }
            }
            return progress;
        }

        /// <summary>
        /// 一批条目的并发下载：校验失败的条目不发请求（标记后导入阶段按失败记录），
        /// 解析异常存 ResolveError 不中断批内其他条目；用户取消 OCE 直接传播。
        /// </summary>
        async Task<PrefetchedItem[]> PrefetchBatchAsync(IReadOnlyList<ModpackItem> batch,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken) {
            PrefetchedItem[] fetched = new PrefetchedItem[batch.Count];
            Task[] downloads = new Task[batch.Count];
            for (int i = 0; i < batch.Count; i++) {
                int index = i;
                ModpackItem item = batch[i];
                downloads[i] = Task.Run(
                    async () => {
                        if (!ModpackRules.TryValidateSource(item, embeddedFiles != null, embeddedFiles, out _)) {
                            fetched[index] = new PrefetchedItem { ValidationFailed = true };
                            return;
                        }
                        try {
                            (byte[] data, string downloadedFileName, string resolvedUrl, PostFileItem communityFile) =
                                await m_sourceResolver.ResolveAsync(item, embeddedFiles, apiBaseUrl, token, cancellationToken);
                            fetched[index] = new PrefetchedItem {
                                Data = data, DownloadedFileName = downloadedFileName, ResolvedUrl = resolvedUrl, CommunityFile = communityFile
                            };
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                            throw;
                        }
                        catch (Exception ex) {
                            fetched[index] = new PrefetchedItem { ResolveError = ex.Message };
                        }
                    },
                    cancellationToken
                );
            }
            await Task.WhenAll(downloads);
            return fetched;
        }

        /// <summary>
        /// 串行导入单个预取条目（校验失败/无链接/跳过/导入/数据文件的原有分类语义不变）。
        /// 返回 false 表示请求已被取代，调用方标记中断并终止整批。
        /// </summary>
        bool ImportFetchedItem(int requestId,
            ModpackItem item,
            PrefetchedItem fetched,
            ModpackDownloadProgress progress,
            ModpackItemDownloadResult itemResult,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            Func<ModpackItem, byte[], ModpackDownloadProgress, byte[]> prepareCustomDataFile,
            Func<ModpackItem, string> noDownloadLinkError,
            Func<ModpackItem, string> unknownItemCategoryError,
            CancellationToken cancellationToken,
            Func<ExternalContentType, string, bool> hasExactLocalContent,
            Func<ModpackItem, string> hostUnavailableError) {
            if (fetched.ValidationFailed) {
                ModpackRules.TryValidateSource(item, embeddedFiles != null, embeddedFiles, out string validationError);
                itemResult.Error = validationError;
                progress.FailedCount++;
                return true;
            }
            if (fetched.ResolveError != null) {
                itemResult.Error = fetched.ResolveError;
                progress.FailedCount++;
                return true;
            }
            if (fetched.Data == null) {
                itemResult.Error = noDownloadLinkError(item);
                progress.FailedCount++;
                return true;
            }
            ExternalContentType contentType = ModpackRules.ResolveContentType(item);
            if (contentType == ExternalContentType.Unknown
                && fetched.CommunityFile != null) {
                contentType = CommunityFileTypes.ToExternal(fetched.CommunityFile.Type);
            }
            ModpackRules.ApplyItemFileInfo(item, fetched.CommunityFile);
            item.Filename = ModpackRules.EnsureItemFileName(item, contentType, fetched.CommunityFile, fetched.DownloadedFileName);
            // ResolveAsync 是 await 点：返回期间用户可能离开界面、请求被取代。复查后再导入，
            // 阻止旧请求继续覆盖写入游戏内容。
            if (!m_isCurrentRequest(requestId)) {
                return false;
            }
            if (contentType != ExternalContentType.Unknown
                && hasExactLocalContent != null
                && hasExactLocalContent(contentType, item.Filename)) {
                // 缺陷 1（默认跳过+完成汇总语义）：帖子整合包管线不做覆盖确认，
                // 撞本地同名内容时跳过该项——不计成功、不计失败、不触发导入，
                // 完成后由控制器按 SkippedCount 汇总提示。
                itemResult.Skipped = true;
                progress.SkippedCount++;
                return true;
            }
            if (contentType != ExternalContentType.Unknown) {
                try {
                    m_itemImporter.ImportExternalContent(requestId, fetched.ResolvedUrl, item.Filename, contentType, fetched.Data, item);
                    itemResult.ImportedName = item.Filename;
                    itemResult.Success = true;
                    progress.SuccessCount++;
                }
                // 用户取消 OCE 不吞成逐项失败（原整批 try 的 when 过滤语义）；超时 OCE 等其余异常仍按逐项失败。
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                    itemResult.Error = ex.Message;
                    progress.FailedCount++;
                }
                return true;
            }
            if (ModpackRules.IsCustomDataFile(item)) {
                try {
                    byte[] dataToImport = prepareCustomDataFile?.Invoke(item, fetched.Data, progress) ?? fetched.Data;
                    if (m_itemImporter.ImportDataFile(dataToImport, item)) {
                        itemResult.Success = true;
                        progress.SuccessCount++;
                    }
                    else {
                        // host 缺失：数据无处导入，按失败计数。
                        itemResult.Error = hostUnavailableError?.Invoke(item) ?? $"Host unavailable: {item.Filename}";
                        progress.FailedCount++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                    itemResult.Error = ex.Message;
                    progress.FailedCount++;
                }
                return true;
            }
            itemResult.Error = unknownItemCategoryError(item);
            progress.FailedCount++;
            return true;
        }
    }
}