using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    internal sealed partial class DownloadFlowManager
    {
        public void StartModpackDownload(PostFileItem file)
        {
            if (ModpackDownloadBusy)
            {
                ShowToast(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackBusyWait"));
                return;
            }

            string fileName = CommunityFileRules.GetDownloadFileName(file);
            string url = file.Url;
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "StartModpackDownload"), url, fileName));
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowToast(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackFileHasNoDownloadLink"), fileName));
                return;
            }

            m_modpackDownloadCompletedAction = null;
            int requestId = ++m_modpackDownloadRequestId;
            m_modpackManifestLoadTask = Task.Run(() => DownloadAndParseModpackManifestAsync(requestId, url, fileName));
        }

        private static async Task<ModpackManifestLoadResult> DownloadAndParseModpackManifestAsync(int requestId, string url, string fileName)
        {
            try
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadModpackManifest"), url));
                (byte[] data, _) = await TransferManager.ModpackSourceResolver.DownloadBytesAsync(url);
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackManifestDownloadCompleted"), data.Length));
                ModpackManifestLoadResult result = new ModpackManifestParser().Parse(requestId, data, url, fileName);
                ModpackManifest manifest = result.Manifest;
                if (manifest == null || manifest.Items == null || manifest.Items.Length == 0)
                {
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackManifestEmptyOrInvalid"), url));
                    return string.IsNullOrWhiteSpace(result.Error)
                        ? new ModpackManifestLoadResult { RequestId = requestId, Error = LanguageControl.Get("ScbbsPlusDownloadFlowManager", "TheModpackManifestIsEmptyOrInvalid") }
                        : result;
                }

                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackSummary"), manifest.Name, manifest.PackageName, manifest.Version, manifest.Items.Length));
                for (int i = 0; i < manifest.Items.Length; i++)
                {
                    ModpackItem item = manifest.Items[i];
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackItemDetails"), i, item.TargetDirectory, item.Filename, item.Url, item.FileId, item.Category, item.TargetPath));
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackManifestDownloadFailed"), url, ex));
                return new ModpackManifestLoadResult { RequestId = requestId, Error = ex.Message };
            }
        }

        private void ConfirmAndProcessModpack(ModpackManifestLoadResult result)
        {
            ModpackManifest manifest = result.Manifest;
            if (manifest == null) return;

            ModpackPresenceSnapshot snapshot = new ModpackPresenceManager().Capture(null);
            string body = BuildModpackConfirmBody(manifest, result.EmbeddedFiles, snapshot, out int needDownloadCount);
            string title = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : result.FileName;

            DialogsManager.ShowDialog(null,
                new MessageDialog(
                    title,
                    body,
                    LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Yes"),
                    LanguageControl.Get("ScbbsPlusDownloadFlowManager", "No"),
                    button =>
                    {
                        if (button == MessageDialogButton.Button1)
                        {
                            // 确认框悬置期间另一入口（本地屏补装/激活）可能已启动子项任务：
                            // 无复检会覆盖 m_modpackItemDownloadTask 引用并抹掉其完成回调
                            //（G3：与 RemoteImportManager 确认路径的同款复检对称）。
                            if (m_modpackItemDownloadTask != null)
                            {
                                ShowToast(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackBusyWait"));
                                return;
                            }
                            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "UserConfirmedModpackDownload"), manifest.Items.Length));
                            int requestId = ++m_modpackDownloadRequestId;
                            m_modpackDownloadCompletedAction = null;
                            m_modpackItemDownloadTask = Task.Run(() => ProcessModpackItemsAsync(
                                requestId,
                                manifest,
                                manifest.Items,
                                result.SourceUrl,
                                result.FileName,
                                result.EmbeddedFiles,
                                saveManifestRecord: true,
                                sourceData: result.OriginalData));
                        }
                        else
                        {
                            // 用户取消/关闭确认框：包体与清单已在本进程，丢弃等于白费这次下载流量。
                            // 只落清单记录（子项一个未下），管理屏出现该包后可从那里补装或删除。
                            // （本体设计偏离模组原版：模组取消即整体丢弃。用户裁定 2026-09-24 选留。）
                            try
                            {
                                ModpackRegistry.Shared.SaveManifest(manifest, result.SourceUrl, result.FileName, result.OriginalData);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(string.Format(CultureInfo.InvariantCulture,
                                    LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackManifestSaveFailed"), ex.Message));
                            }
                        }
                    }));
        }

        /// <summary>
        /// 确认框正文：分叉问句（需下载为零则不提下载）+ 逐子项三态清单
        /// （已存在=本机已有同内容；内置=随包 zip 落盘；需下载=需网络获取）。
        /// </summary>
        private static string BuildModpackConfirmBody(
            ModpackManifest manifest,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            ModpackPresenceSnapshot snapshot,
            out int needDownloadCount)
        {
            needDownloadCount = 0;
            List<string> lines = [];
            foreach (ModpackItem item in manifest.Items ?? [])
            {
                if (item == null || !item.IsEnabled)
                {
                    continue;
                }

                bool bundled = IsBundledItem(item, embeddedFiles);
                bool installed = !bundled && ModpackPresenceRules.IsItemInstalled(item, snapshot);
                string state = bundled
                    ? LanguageControl.Get("ModpackDetailsDialog", "StateBundled")
                    : installed
                        ? LanguageControl.Get("ModpackDetailsDialog", "StateExists")
                        : LanguageControl.Get("ModpackDetailsDialog", "StateNeedDownload");
                if (!bundled && !installed)
                {
                    needDownloadCount++;
                }

                ExternalContentType contentType = ModpackRules.ResolveContentType(item);
                string typeName = LanguageControl.GetEnumDisplayName(
                    CommunityFileRules.MapExternalContentTypeToFileType(contentType),
                    item?.Category);
                lines.Add($"- [{state}][{typeName}] {ModpacksManageContentScreen.GetModpackItemDisplayName(item)}");
            }

            lines.Insert(0, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "SubItemsHeader"));
            lines.Insert(0, string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusDownloadFlowManager", needDownloadCount > 0 ? "ConfirmDownloadAndEnable" : "ConfirmEnableAllReady"),
                needDownloadCount));
            return string.Join("\n", lines);
        }

        private static bool IsBundledItem(ModpackItem item, IReadOnlyDictionary<string, byte[]> embeddedFiles)
        {
            string url = item?.Url?.Trim();
            return !string.IsNullOrWhiteSpace(url)
                && !ModpackRules.IsHttpUrl(url)
                && ModpackRules.TryGetEmbeddedFile(embeddedFiles, url, out _);
        }

        /// <summary>
        /// 启动整合包子项处理任务（本地屏整合包激活/缺项下载也走这条管线）。
        /// 返回 false 表示忙时静默拒启（仅弹忙 Toast），调用方须跳过各自的启动提示。
        /// </summary>
        public bool StartModpackItemsDownload(
            ModpackManifest manifest,
            IEnumerable<ModpackItem> itemsToProcess,
            string sourceUrl,
            string sourceFileName,
            IReadOnlyDictionary<string, byte[]> embeddedFiles = null,
            bool saveManifestRecord = false,
            byte[] sourceData = null,
            Func<ModpackItem, byte[], ModpackDownloadProgress, byte[]> customFileBackupPreparer = null,
            Action<ModpackDownloadProgress> completedAction = null,
            bool allowStartAfterDownloadFailures = false)
        {
            // 与 StartModpackDownload 同款守卫：控制器是进程级单例，社区屏子项在途时
            // 本地屏激活/补装进入会覆盖 m_modpackItemDownloadTask 引用与
            // m_modpackDownloadCompletedAction，旧调用方的收尾回调（列表刷新/备份还原）被静默丢弃。
            if (ModpackDownloadBusy)
            {
                ShowToast(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackBusyWait"));
                return false;
            }
            m_modpackDownloadCompletedAction = completedAction;
            m_modpackAllowStartAfterDownloadFailures = allowStartAfterDownloadFailures;
            int requestId = ++m_modpackDownloadRequestId;
            m_modpackItemDownloadTask = Task.Run(() => ProcessModpackItemsAsync(
                requestId,
                manifest,
                itemsToProcess,
                sourceUrl,
                sourceFileName,
                embeddedFiles,
                saveManifestRecord,
                sourceData,
                customFileBackupPreparer));
            return true;
        }

        private async Task<ModpackDownloadProgress> ProcessModpackItemsAsync(
            int requestId,
            ModpackManifest manifest,
            IEnumerable<ModpackItem> itemsToProcess,
            string sourceUrl,
            string sourceFileName,
            IReadOnlyDictionary<string, byte[]> embeddedFiles = null,
            bool saveManifestRecord = true,
            byte[] sourceData = null,
            Func<ModpackItem, byte[], ModpackDownloadProgress, byte[]> customFileBackupPreparer = null)
        {
            Log.Information(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "StartProcessingModpackItems"));
            try
            {
                ModpackDownloadProgress progress = await m_modpackDownloader.ExecuteAsync(
                    requestId,
                    itemsToProcess ?? manifest?.Items,
                    embeddedFiles,
                    AuthSessionManager.Instance.ApiBaseUrl,
                    AuthSessionManager.Instance.CurrentToken,
                    customFileBackupPreparer,
                    item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "HasNoDownloadLinkSkipped"), item.Filename),
                    item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "UnknownItemCategory"), item.Category),
                    hostUnavailableError: item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DataFileHostUnavailable"), item.Filename),
                    hasExactLocalContent: (type, fileName) =>
                        // 与导入覆盖判定同口径（缺陷 1 补审 F1）：家具/材质/皮肤走
                        // FindExistingExternalContentName 的归一匹配，Mod 补 ModsPath 文件名直查。
                        // World 不参与判定：游戏原生导入总建新目录（World/World1/…自动编号），
                        // 重复导入即天然副本，整合包的世界项每次都重新下载导入。
                        ExternalContentManager.FindExistingExternalContentName(type, fileName) != null
                        || LocalModStateStore.FindExactLocalDownloadFilePath(type, fileName) != null);

                if (saveManifestRecord)
                {
                    ModpackRegistry.Shared.SaveManifest(manifest, sourceUrl, sourceFileName, sourceData);
                }
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackProcessingCompleted"), progress.SuccessCount, progress.FailedCount));
                return progress;
            }
            catch (Exception ex)
            {
                // ExecuteAsync 入口（如 GetItemFileName 在逐项 try 之外）或 SaveManifest 文件 I/O 抛出
                // 会让任务 Faulted，消费端读 .Result 冒泡出 Update()。兜住返回带 Error 的失败进度。
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ModpackProcessingFaulted"), ex.Message));
                return new ModpackDownloadProgress
                {
                    Error = ex.Message,
                    Results = []
                };
            }
        }

        internal static string BuildModpackDownloadFailureMessage(ModpackDownloadProgress progress, string summary)
        {
            StringBuilder builder = new(summary ?? string.Empty);
            int displayedCount = 0;
            foreach (ModpackItemDownloadResult result in progress?.Results ?? [])
            {
                // 跳过项（本地同名，缺陷 1 汇总语义）不算失败，不进失败对话框。
                if (result == null || result.Success || result.Skipped)
                {
                    continue;
                }

                string itemName = result.Item?.Filename
                    ?? result.Item?.FileId
                    ?? result.Item?.Url
                    ?? LanguageControl.Get("ScbbsPlusDownloadFlowManager", "UnknownResource");
                builder.Append("\n\n").Append(itemName);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    builder.Append("：").Append(result.Error);
                }
                if (++displayedCount >= 5)
                {
                    break;
                }
            }
            return builder.ToString();
        }

        internal static string GetPlatformName()
        {
            // 该值直进服务器反馈载荷（download/record 与上传登记）：
            // 用稳定平台标识，不走本地化——zh-CN 下曾返回"安卓"，
            // 服务器按 Platform 分组的统计口径永久分裂。
            if (OperatingSystem.IsAndroid())
            {
                return "Android";
            }
            if (OperatingSystem.IsWindows())
            {
                return "Windows";
            }
            if (OperatingSystem.IsIOS())
            {
                return "iOS";
            }
            if (OperatingSystem.IsMacOS())
            {
                return "macOS";
            }
            if (OperatingSystem.IsLinux())
            {
                return "Linux";
            }
            return string.Empty;
        }

    }
}