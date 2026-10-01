namespace Game {
    sealed class RemoteModpackInstaller {
        readonly IModpackSourceResolver m_sourceResolver;
        readonly IModpackItemImporter m_itemImporter;
        readonly Func<ExternalContentType, string, bool> m_hasExactLocalContent;
        readonly Func<ModpackItem, bool> m_isModpackDataFilePresent;
        readonly Func<string, Task<ModpackFileConflictAction>> m_requestConflictAction;
        readonly Func<int, bool> m_isCurrentRequest;
        readonly Action<ModpackItem, int, int> m_reportProgress;
        readonly Func<ModpackItem, string> m_noDownloadLinkError;
        readonly Func<ModpackItem, string> m_unknownItemCategoryError;
        readonly Func<ModpackItem, string> m_hostUnavailableError;

        public RemoteModpackInstaller(IModpackSourceResolver sourceResolver,
            IModpackItemImporter itemImporter,
            Func<ExternalContentType, string, bool> hasExactLocalContent,
            Func<ModpackItem, bool> isModpackDataFilePresent,
            Func<string, Task<ModpackFileConflictAction>> requestConflictAction,
            Func<int, bool> isCurrentRequest,
            Action<ModpackItem, int, int> reportProgress,
            Func<ModpackItem, string> noDownloadLinkError,
            Func<ModpackItem, string> unknownItemCategoryError,
            Func<ModpackItem, string> hostUnavailableError) {
            m_sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
            m_itemImporter = itemImporter ?? throw new ArgumentNullException(nameof(itemImporter));
            m_hasExactLocalContent = hasExactLocalContent ?? throw new ArgumentNullException(nameof(hasExactLocalContent));
            m_isModpackDataFilePresent = isModpackDataFilePresent ?? throw new ArgumentNullException(nameof(isModpackDataFilePresent));
            m_requestConflictAction = requestConflictAction ?? throw new ArgumentNullException(nameof(requestConflictAction));
            m_isCurrentRequest = isCurrentRequest ?? throw new ArgumentNullException(nameof(isCurrentRequest));
            m_reportProgress = reportProgress ?? throw new ArgumentNullException(nameof(reportProgress));
            m_noDownloadLinkError = noDownloadLinkError ?? throw new ArgumentNullException(nameof(noDownloadLinkError));
            m_unknownItemCategoryError = unknownItemCategoryError ?? throw new ArgumentNullException(nameof(unknownItemCategoryError));
            m_hostUnavailableError = hostUnavailableError ?? throw new ArgumentNullException(nameof(hostUnavailableError));
        }

        public async Task<RemoteModpackResult> ExecuteAsync(int requestId,
            ModpackManifest manifest,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default) {
            RemoteModpackResult result = new();
            if (manifest?.Items == null) {
                return result;
            }
            bool skipAllConflicts = false;
            bool overwriteAllConflicts = false;
            HashSet<ModpackItem> skippedItems = [];
            HashSet<ModpackItem> overwriteItems = [];
            foreach (ModpackItem item in manifest.Items) {
                if (item is null or { IsEnabled: false }) {
                    continue;
                }
                if (!m_isCurrentRequest(requestId)) {
                    // 请求被取代：停止预取，不为已废弃请求继续发起网络请求。
                    result.Interrupted = true;
                    break;
                }
                try {
                    item.Filename = ModpackRules.GetItemFileName(item);
                    ExternalContentType contentType = ModpackRules.ResolveContentType(item);
                    PostFileItem communityFile = null;
                    if (!string.IsNullOrWhiteSpace(item.FileId)
                        && (!item.HasExplicitFileName || contentType == ExternalContentType.Unknown)) {
                        communityFile = await m_sourceResolver.FetchFileInfoByIdAsync(apiBaseUrl, token, item.FileId, cancellationToken);
                        ModpackRules.ApplyItemFileInfo(item, communityFile);
                        if (contentType == ExternalContentType.Unknown
                            && communityFile != null) {
                            contentType = CommunityFileTypes.ToExternal(communityFile.Type);
                        }
                        // 预检已取得下载链接：填充 Url，导入阶段 ResolveAsync 直接走下载分支，
                        // 不再重复请求 fileId 元数据（原每次导入都二次 /upload/info）。
                        if (string.IsNullOrWhiteSpace(item.Url)
                            && !string.IsNullOrWhiteSpace(communityFile?.Url)) {
                            item.Url = communityFile.Url;
                        }
                    }
                    item.Filename = ModpackRules.EnsureItemFileName(item, contentType, communityFile);
                    if (!HasExactLocalFile(contentType, item)) {
                        continue;
                    }
                    if (overwriteAllConflicts) {
                        overwriteItems.Add(item);
                        continue;
                    }
                    if (skipAllConflicts) {
                        skippedItems.Add(item);
                        continue;
                    }
                    ModpackFileConflictAction action = await m_requestConflictAction(item.Filename);
                    if (action == ModpackFileConflictAction.Close) {
                        result.Interrupted = true;
                        return result;
                    }
                    if (action == ModpackFileConflictAction.Skip
                        || action == ModpackFileConflictAction.SkipAll) {
                        skipAllConflicts = action == ModpackFileConflictAction.SkipAll;
                        skippedItems.Add(item);
                    }
                    else {
                        overwriteAllConflicts = action == ModpackFileConflictAction.OverwriteAll;
                        overwriteItems.Add(item);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // 用户取消：传播中止整批；超时 OCE 不满足 when，仍按预检非致命处理。
                    throw;
                }
                catch {
                    // Keep preflight failures non-fatal: the import pass retries and reports them per item.
                }
            }
            for (int i = 0; i < manifest.Items.Length; i++) {
                ModpackItem item = manifest.Items[i];
                if (item is null or { IsEnabled: false }) {
                    continue;
                }
                if (!m_isCurrentRequest(requestId)) {
                    // 请求被取代：置位 Interrupted，调用方按"部分导入的中断结果"处理而非成功。
                    result.Interrupted = true;
                    break;
                }
                if (skippedItems.Contains(item)) {
                    result.SkippedCount++;
                    continue;
                }
                item.Filename = ModpackRules.GetItemFileName(item);
                m_reportProgress(item, i + 1, manifest.Items.Length);
                try {
                    if (!ModpackRules.TryValidateSource(item, embeddedFiles != null, embeddedFiles, out string validationError)) {
                        // 多项失败只保留首个原因（首因原则），后续失败不再覆盖诊断信息。
                        result.LastError ??= validationError;
                        result.FailedCount++;
                        continue;
                    }
                    (byte[] data, string downloadedFileName, string itemUrl, PostFileItem communityFile) = await m_sourceResolver.ResolveAsync(
                        item,
                        embeddedFiles,
                        apiBaseUrl,
                        token,
                        cancellationToken
                    );
                    if (data == null) {
                        // 多项失败只保留首个原因（首因原则），后续失败不再覆盖诊断信息。
                        result.LastError ??= m_noDownloadLinkError(item);
                        result.FailedCount++;
                        continue;
                    }
                    ExternalContentType contentType = ModpackRules.ResolveContentType(item);
                    if (contentType == ExternalContentType.Unknown
                        && communityFile != null) {
                        contentType = CommunityFileTypes.ToExternal(communityFile.Type);
                    }
                    ModpackRules.ApplyItemFileInfo(item, communityFile);
                    item.Filename = ModpackRules.EnsureItemFileName(item, contentType, communityFile, downloadedFileName);
                    if (HasExactLocalFile(contentType, item)
                        && !overwriteAllConflicts
                        && !overwriteItems.Contains(item)) {
                        if (skipAllConflicts) {
                            result.SkippedCount++;
                            continue;
                        }
                        ModpackFileConflictAction action = await m_requestConflictAction(item.Filename);
                        if (action == ModpackFileConflictAction.Close) {
                            result.Interrupted = true;
                            break;
                        }
                        if (action == ModpackFileConflictAction.Skip
                            || action == ModpackFileConflictAction.SkipAll) {
                            skipAllConflicts = action == ModpackFileConflictAction.SkipAll;
                            result.SkippedCount++;
                            continue;
                        }
                        overwriteAllConflicts = action == ModpackFileConflictAction.OverwriteAll;
                    }

                    // ResolveAsync 与 m_requestConflictAction 都是 await 点：返回期间请求可能被取代
                    // （用户离开界面）。复查后再导入，阻止旧请求覆盖写入游戏内容。
                    if (!m_isCurrentRequest(requestId)) {
                        result.Interrupted = true;
                        break;
                    }
                    if (contentType != ExternalContentType.Unknown) {
                        m_itemImporter.ImportExternalContent(requestId, itemUrl, item.Filename, contentType, data, item);
                        result.SuccessCount++;
                    }
                    else if (ModpackRules.IsCustomDataFile(item)) {
                        if (m_itemImporter.ImportDataFile(data, item)) {
                            result.SuccessCount++;
                        }
                        else {
                            // host 缺失：数据无处导入，按失败计数。
                            result.LastError ??= m_hostUnavailableError(item);
                            result.FailedCount++;
                        }
                    }
                    else {
                        // 多项失败只保留首个原因（首因原则），后续失败不再覆盖诊断信息。
                        result.LastError ??= m_unknownItemCategoryError(item);
                        result.FailedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // 用户取消：传播中止整批；超时 OCE 不满足 when，仍按逐项失败记录。
                    throw;
                }
                catch (Exception ex) {
                    // 多项失败只保留首个原因（首因原则），后续失败不再覆盖诊断信息。
                    result.LastError ??= ex.Message;
                    result.FailedCount++;
                }
            }
            return result;
        }

        bool HasExactLocalFile(ExternalContentType contentType, ModpackItem item) => contentType != ExternalContentType.Unknown
            ? m_hasExactLocalContent(contentType, item.Filename)
            : ModpackRules.IsCustomDataFile(item) && m_isModpackDataFilePresent(item);
    }

    sealed class RemoteModpackResult {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public string LastError { get; set; }
        public bool Interrupted { get; set; }
    }
}