using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    public static partial class RemoteImportManager
    {
        private static readonly HttpClient s_httpClient = HttpClientProvider.RemoteImport;
        private static readonly CommunityHttpDownloader s_httpDownloader = new(s_httpClient);
        private static readonly ModpackSourceResolver s_modpackSourceResolver =
            new(s_httpClient, new CommunityApiClient(HttpClientProvider.Community));
        private static CommunityTransferProgress s_progress;
        private static CommunityDownloadDialog s_downloadDialog;
        private static Task<ExternalContentPreparation> s_prepareTask;
        private static Task<CommunityFileDownloadResult> s_importTask;
        private static int s_prepareRequestId;
        private static bool s_showDuplicatePrompt = true;
        private static readonly ModpackConflictManager s_modpackConflictManager = new();
        private static IRemoteImportHost s_host;
        private static readonly ModpackManifestParser s_manifestParser = new();
        private static readonly ModpackItemImporter s_modpackItemImporter =
            new(ModIdentity.PackageName, () => s_host);
        private static readonly RemoteModpackInstaller s_modpackInstaller =
            new(
                s_modpackSourceResolver,
                s_modpackItemImporter,
                (type, fileName) => s_host?.HasExactLocalContent(type, fileName) == true,
                item => s_host?.IsModpackDataFilePresent(item) == true,
                fileName => s_modpackConflictManager.RequestAsync(fileName),
                requestId => requestId == s_prepareRequestId,
                (item, itemIndex, totalCount) => s_progress.Set(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "Downloading"),
                        item.Filename ?? item.FileId ?? item.Url,
                        itemIndex,
                        totalCount),
                    (float)(itemIndex - 1) / totalCount,
                    true),
                item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "HasNoDownloadLinkSkipped"), item.Filename),
                item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "UnknownItemCategory"), item.Category),
                item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "DataFileHostUnavailable"), item.Filename));

        static RemoteImportManager()
        {
            s_modpackConflictManager.RequestCreated += OnModpackConflictRequested;
        }

        internal static void RegisterHost(IRemoteImportHost host)
        {
            s_host = host;
        }

        internal static void UnregisterHost(IRemoteImportHost host)
        {
            if (ReferenceEquals(s_host, host))
            {
                s_host = null;
            }
        }

        public static void OpenImportLinkDialog()
        {
            DialogsManager.ShowDialog(
                null,
                new DownloadContentFromLinkDialog(
                    string.Empty,
                    StartExternalContentPreparation));
        }

        public static void StartExternalContentPreparation(string rawUrl, string fileName)
        {
            StartExternalContentPreparation(rawUrl, fileName, showDuplicatePrompt: true);
        }

        public static void StartScbbsPlusUpdateDownload(string rawUrl, string fileName)
        {
            StartExternalContentPreparation(rawUrl, fileName, showDuplicatePrompt: false);
        }
        public static bool IsBusy()
        {
            return s_prepareTask != null || s_importTask != null;
        }

        private static void OnModpackConflictRequested(ModpackConflictRequest request)
        {
            if (!s_modpackConflictManager.TryMarkRequestDisplayed(request))
            {
                return;
            }

            Dispatcher.Dispatch(() =>
            {
                try
                {
                    DialogsManager.ShowDialog(
                        null,
                        new CommunityModpackFileConflictDialog(
                            request.FileName,
                            action =>
                            {
                                s_modpackConflictManager.Complete(request, action);
                            }));
                }
                catch (Exception ex)
                {
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ShowConflictDialogFailed"), ex));
                    s_modpackConflictManager.Complete(request, ModpackFileConflictAction.Skip);
                    DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, ex.Message, LanguageControl.Ok, null, null));
                }
            });
        }

        private static void AttachPrepareCompletion(Task<ExternalContentPreparation> task, int requestId)
        {
            _ = task.ContinueWith(
                completed => Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        HandlePrepareTaskCompleted(completed, requestId);
                    }
                    catch (Exception ex)
                    {
                        HandleUiCompletionFailure(ex);
                    }
                }),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void AttachImportCompletion(Task<CommunityFileDownloadResult> task, int requestId)
        {
            _ = task.ContinueWith(
                completed => Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        HandleImportTaskCompleted(completed, requestId);
                    }
                    catch (Exception ex)
                    {
                        HandleUiCompletionFailure(ex);
                    }
                }),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void HandlePrepareTaskCompleted(Task<ExternalContentPreparation> completedTask, int requestId)
        {
            if (!ReferenceEquals(s_prepareTask, completedTask))
            {
                return;
            }

            ExternalContentPreparation result;
            try
            {
                result = completedTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "RemoteImportPrepareTaskFaulted"), ex));
                result = new ExternalContentPreparation { RequestId = requestId, Error = ex.GetBaseException().Message };
            }
            s_prepareTask = null;
            if (result == null || result.RequestId != s_prepareRequestId)
            {
                return;
            }

            if (result.ArchiveSaved)
            {
                if (s_downloadDialog != null)
                {
                    DialogsManager.HideDialog(s_downloadDialog);
                    s_downloadDialog = null;
                }
                ConfirmArchiveOverwriteAndSave(requestId, result.Url, result.FileName, result.ArchiveData);
                return;
            }

            if (result.Manifest != null)
            {
                if (s_downloadDialog != null)
                {
                    DialogsManager.HideDialog(s_downloadDialog);
                    s_downloadDialog = null;
                }
                ConfirmRemoteModpackOverwrite(requestId, result.Manifest, result.Url, result.FileName, result.EmbeddedFiles, result.OriginalData);
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                s_downloadDialog?.SetFailed(result.Error);
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, result.Error, LanguageControl.Ok, null, null));
                return;
            }

            if (s_showDuplicatePrompt && result.RequiresOverwriteConfirmation)
            {
                if (s_downloadDialog != null)
                {
                    DialogsManager.HideDialog(s_downloadDialog);
                    s_downloadDialog = null;
                }

                string resourceTypeName = ExternalContentManager.GetContentTypeDisplayName(result.Type);
                string existingName = result.ExistingDisplayName ?? result.ExistingEntryName ?? result.FileName;
                string overwriteMessage = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "LocalAlreadyExistsOverwriteIt"), resourceTypeName, existingName);
                // 仅家具包提供"导入为副本"：引擎重名时自动另起新名保留旧包；
                // 材质/皮肤是原地覆盖、Mod 抛同名异常，均无安全的副本语义。
                bool canRename = result.Type == ExternalContentType.FurniturePack;
                DialogsManager.ShowDialog(
                    null,
                    new CommunityImportOverwriteDialog(overwriteMessage, canRename, action =>
                    {
                        if (result.RequestId != s_prepareRequestId || s_importTask != null)
                        {
                            return;
                        }
                        switch (action)
                        {
                            case ImportOverwriteAction.Overwrite:
                                StartPreparedRemoteImport(result, overwriteExisting: true);
                                break;
                            case ImportOverwriteAction.Rename:
                                StartPreparedRemoteImport(result, overwriteExisting: false);
                                break;
                        }
                    }));
                return;
            }

            StartPreparedRemoteImport(result, overwriteExisting: false);
        }

        private static void HandleImportTaskCompleted(Task<CommunityFileDownloadResult> completedTask, int requestId)
        {
            if (!ReferenceEquals(s_importTask, completedTask))
            {
                return;
            }

            CommunityFileDownloadResult result;
            try
            {
                result = completedTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "RemoteImportTaskFaulted"), ex));
                result = new CommunityFileDownloadResult { Error = ex.GetBaseException().Message };
            }
            s_importTask = null;
            if (requestId != s_prepareRequestId || result == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(result.Error))
            {
                if (result.Canceled)
                {
                    HideRemoteImportDialog();
                    DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("RemoteImportManager", "Modpack"), result.ImportedName ?? LanguageControl.Get("RemoteImportManager", "DownloadInterruptedManifestKept"), LanguageControl.Ok, null, null));
                    return;
                }

                s_progress?.Set(result.ImportedName ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "Imported"), result.FileName), 1f, false);
                if (result.HideProgressDialog)
                {
                    HideRemoteImportDialog();
                }
                else
                {
                    s_downloadDialog?.SetCompleted(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "Imported"), result.FileName));
                }
                if (result.RequiresRestart)
                {
                    RestartConfirmDialog.ShowModImportRestartConfirm(result.FileName, "remote-import");
                }
                else
                {
                    DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("RemoteImportManager", "DownloadComplete"), string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "Imported"), result.FileName), LanguageControl.Ok, null, null));
                }
            }
            else
            {
                s_downloadDialog?.SetFailed(result.Error);
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, result.Error, LanguageControl.Ok, null, null));
            }
        }

        private static void HideRemoteImportDialog()
        {
            if (s_downloadDialog != null)
            {
                DialogsManager.HideDialog(s_downloadDialog);
                s_downloadDialog = null;
            }
        }

        private static void HandleUiCompletionFailure(Exception exception)
        {
            Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "RemoteImportCompletionFaulted"), exception));
            HideRemoteImportDialog();
            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, exception.GetBaseException().Message, LanguageControl.Ok, null, null));
        }

        private static void StartExternalContentPreparation(string rawUrl)
        {
            StartExternalContentPreparation(rawUrl, null, showDuplicatePrompt: true);
        }

        private static void StartExternalContentPreparation(string rawUrl, string fileName, bool showDuplicatePrompt)
        {
            if (s_prepareTask != null || s_importTask != null)
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, LanguageControl.Get("RemoteImportManager", "ImportOrDownloadBusy"), LanguageControl.Ok, null, null));
                return;
            }

            // 整合包分支同样消费此旗标（同名覆盖确认），必须在分叉前赋值，
            // 否则读到上一次资源导入的残留值。
            s_showDuplicatePrompt = showDuplicatePrompt;

            try
            {
                string candidateFileName = ModpackRules.TryGetPathTailName(fileName ?? rawUrl);
                string extension = Storage.GetExtensionBCL(candidateFileName ?? string.Empty).ToLowerInvariant();
                if (extension == ".zip" || extension == ".json")
                {
                    StartRemoteModpackImport(rawUrl, fileName ?? candidateFileName);
                    return;
                }

                RemoteImportRequest request = CreateRequest(rawUrl, fileName);
                int requestId = ++s_prepareRequestId;
                s_progress = new CommunityTransferProgress();
                s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("RemoteImportManager", "ImportResource"), progress: s_progress);
                s_downloadDialog.SetProgress(s_progress);
                DialogsManager.ShowDialog(null, s_downloadDialog);
                s_prepareTask = Task.Run(async () =>
                {
                    try
                    {
                        string serverFileName = null;
                        using MemoryStream stream = await DownloadToMemoryAsync(request.Url, request.FileName, s_progress, name => serverFileName = name);
                        s_progress.Set(LanguageControl.Get("RemoteImportManager", "CheckingLocalResources"), 1f, false);
                        ApplyServerFileName(request, serverFileName);
                        if (request.Type == ExternalContentType.Unknown)
                        {
                            return new ExternalContentPreparation
                            {
                                RequestId = requestId,
                                FileName = request.FileName,
                                Type = request.Type,
                                Error = LanguageControl.Get("RemoteImportManager", "UnsupportedFileExtension")
                            };
                        }
                        return ExternalContentManager.PrepareExternalContent(ModIdentity.PackageName, requestId, request, stream);
                    }
                    catch (Exception ex)
                    {
                        return new ExternalContentPreparation
                        {
                            RequestId = requestId,
                            FileName = request.FileName,
                            Type = request.Type,
                            Error = ex.Message
                        };
                    }
                });
                AttachPrepareCompletion(s_prepareTask, requestId);
            }
            catch (Exception ex)
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, ex.Message, LanguageControl.Ok, null, null));
            }
        }

        private static void StartPreparedRemoteImport(ExternalContentPreparation result, bool overwriteExisting)
        {
            if (result?.FileData == null)
            {
                return;
            }

            if (s_downloadDialog == null)
            {
                s_progress = new CommunityTransferProgress();
                s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("RemoteImportManager", "ImportResource"), progress: s_progress);
                s_downloadDialog.SetProgress(s_progress);
                DialogsManager.ShowDialog(null, s_downloadDialog);
            }

            s_progress?.Set(LanguageControl.Get("RemoteImportManager", "Importing"), 1f, false);
            s_importTask = Task.Run(() =>
            {
                try
                {
                    using MemoryStream stream = new(result.FileData, writable: false);
                    string importedName = ExternalContentManager.ImportPreparedExternalContent(ModIdentity.PackageName, stream, result, overwriteExisting);
                    return new CommunityFileDownloadResult
                    {
                        FileName = result.FileName,
                        ImportedName = importedName,
                        Type = result.Type,
                        RequiresRestart = result.RequiresRestart
                    };
                }
                catch (InvalidScmodArchiveException)
                {
                    // 归档结构无效（G2 类型化分类）：坏包按失败计、展示统一文案。
                    return new CommunityFileDownloadResult
                    {
                        FileName = result.FileName,
                        DownloadFailed = CommunityFileTypes.IsModLike(result.Type),
                        Error = ScmodArchiveReader.InvalidArchiveFriendlyMessage
                    };
                }
                catch (Exception ex)
                {
                    return new CommunityFileDownloadResult
                    {
                        FileName = result.FileName,
                        DownloadFailed = false,
                        Error = ex.Message
                    };
                }
            });
            AttachImportCompletion(s_importTask, s_prepareRequestId);
        }

        // 类型待定（URL 无扩展名）时按响应头文件名二次定型；URL 已识别出类型则不
        // 覆盖。服务器名过 CreateRequest 同款归一与穿越校验，识别仍失败维持 Unknown，
        // 由 PrepareExternalContent 按不支持类型统一报错。
        private static void ApplyServerFileName(RemoteImportRequest request, string serverFileName)
        {
            if (request.Type != ExternalContentType.Unknown || string.IsNullOrWhiteSpace(serverFileName))
            {
                return;
            }
            string fileName = Storage.GetFileName(serverFileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0
                || fileName == "."
                || fileName == "..")
            {
                return;
            }
            ExternalContentType type = ResolveType(fileName);
            if (type == ExternalContentType.Unknown)
            {
                return;
            }
            request.FileName = fileName;
            request.Type = type;
        }

        private static async Task<MemoryStream> DownloadToMemoryAsync(string url, string fileName, CommunityTransferProgress progress, Action<string> serverFileName = null)
        {
            progress?.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "DownloadingFormat"), fileName), 0f, true);
            Progress<DownloadProgress> byteProgress = new(info =>
            {
                if (info.TotalBytes is > 0)
                {
                    progress?.Set(
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "DownloadingFormat"), fileName),
                        info.BytesDownloaded / (float)info.TotalBytes.Value,
                        true);
                }
                else
                {
                    progress?.Set(LanguageControl.Get("RemoteImportManager", "DownloadingProgress"), 0f, false);
                }
            });
            try
            {
                return await s_httpDownloader.DownloadToMemoryAsync(url, null, CancellationToken.None, byteProgress, serverFileName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "DownloadFailedFormat"), ex.Message), ex);
            }
        }

        private static void StartRemoteModpackImport(string url, string fileName)
        {
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "StartModpackImport"), url, fileName));
            bool downloadArchiveOnly = string.Equals(Storage.GetExtensionBCL(fileName), ".zip", StringComparison.OrdinalIgnoreCase);

            s_progress = new CommunityTransferProgress();
            s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("RemoteImportManager", "ImportModpack"), progress: s_progress);
            s_downloadDialog.SetProgress(s_progress);
            DialogsManager.ShowDialog(null, s_downloadDialog);

            int requestId = ++s_prepareRequestId;
            s_prepareTask = Task.Run(async () =>
            {
                string kindWord = downloadArchiveOnly ? string.Empty : LanguageControl.Get("RemoteImportManager", "Manifest");
                try
                {
                    s_progress.Set(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            downloadArchiveOnly
                                ? LanguageControl.Get("RemoteImportManager", "DownloadArchiveFormat")
                                : LanguageControl.Get("RemoteImportManager", "DownloadManifestFormat"),
                            fileName),
                        0f,
                        true);
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "DownloadModpackPackage"), kindWord, url));
                    Progress<DownloadProgress> byteProgress = new(info =>
                    {
                        if (info.TotalBytes is > 0)
                        {
                            s_progress.Set(
                                string.Format(CultureInfo.InvariantCulture,
                                    downloadArchiveOnly
                                        ? LanguageControl.Get("RemoteImportManager", "DownloadArchiveFormat")
                                        : LanguageControl.Get("RemoteImportManager", "DownloadManifestFormat"),
                                    fileName),
                                info.BytesDownloaded / (float)info.TotalBytes.Value,
                                true);
                        }
                    });
                    using MemoryStream dataStream = await s_httpDownloader.DownloadToMemoryAsync(url, null, CancellationToken.None, byteProgress);
                    byte[] data = dataStream.ToArray();
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackPackageDownloadCompleted"), kindWord, data.Length));
                    if (downloadArchiveOnly)
                    {
                        // 落盘改在主线程覆盖确认后执行（SaveRemoteArchive），准备任务只携带字节。
                        // Url 必须随行：SaveArchive 写溯源 meta，缺失会永久丢失同源判定证据。
                        return new ExternalContentPreparation { RequestId = requestId, Url = url, FileName = fileName, ArchiveSaved = true, ArchiveData = data };
                    }

                    ModpackManifestLoadResult manifestResult = s_manifestParser.Parse(requestId, data, url, fileName);
                    ModpackManifest manifest = manifestResult.Manifest;

                    if (manifest == null || manifest.Items == null || manifest.Items.Length == 0)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackManifestEmptyOrInvalid"), url));
                        return new ExternalContentPreparation
                        {
                            RequestId = requestId,
                            FileName = fileName,
                            Error = manifestResult.Error ?? LanguageControl.Get("RemoteImportManager", "TheModpackManifestIsEmptyOrInvalid")
                        };
                    }

                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackSummary"), manifest.Name, manifest.PackageName, manifest.Version, manifest.Items.Length));
                    for (int i = 0; i < manifest.Items.Length; i++)
                    {
                        ModpackItem item = manifest.Items[i];
                        Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackItemDetails"), i, item.TargetDirectory, item.Filename, item.Url, item.FileId, item.Category, item.TargetPath));
                    }

                    return new ExternalContentPreparation
                    {
                        RequestId = requestId,
                        Url = url,
                        FileName = fileName,
                        Manifest = manifest,
                        EmbeddedFiles = manifestResult.EmbeddedFiles,
                        OriginalData = manifestResult.OriginalData
                    };
                }
                catch (Exception ex)
                {
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackManifestDownloadFailed"), url, ex));
                    return new ExternalContentPreparation { RequestId = requestId, FileName = fileName, Error = ex.Message };
                }
            });
            AttachPrepareCompletion(s_prepareTask, requestId);
        }

        // 直链 zip 整合包：与清单链同款覆盖确认（同名检查 → CommunityImportOverwriteDialog），
        // 确认后主线程 SaveArchive 落盘并沿用「已保存到本地」完成弹窗。
        private static void ConfirmArchiveOverwriteAndSave(int requestId, string url, string fileName, byte[] archiveData)
        {
            string conflictingPath = ModpackRegistry.Shared.FindConflictingPackPath(fileName);
            if (conflictingPath != null && s_showDuplicatePrompt)
            {
                string message = string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("RemoteImportManager", "LocalAlreadyExistsOverwriteIt"),
                    LanguageControl.Get("RemoteImportManager", "Modpack"),
                    Storage.GetFileName(conflictingPath));
                DialogsManager.ShowDialog(
                    null,
                    new CommunityImportOverwriteDialog(message, canRename: false, action =>
                    {
                        if (action == ImportOverwriteAction.Overwrite)
                        {
                            SaveRemoteArchive(requestId, url, fileName, archiveData, overwriteConflicting: true);
                        }
                        // 取消：一个字节未落盘，零残留。
                    }));
                return;
            }

            SaveRemoteArchive(requestId, url, fileName, archiveData, overwriteConflicting: false);
        }

        private static void SaveRemoteArchive(int requestId, string url, string fileName, byte[] archiveData, bool overwriteConflicting)
        {
            // 确认框可能悬置到下一次导入之后才被点击：过期 requestId 或已有导入在跑时丢弃本次落盘。
            if (requestId != s_prepareRequestId || s_importTask != null)
            {
                return;
            }
            try
            {
                ModpackRegistry.Shared.SaveArchive(archiveData, url, fileName, overwriteConflicting);
            }
            catch (Exception ex)
            {
                // 原实现此写入在准备任务内、异常走统一错误弹窗；搬进按钮回调后自兜同款，
                // 不允许磁盘满等异常从对话框按钮处理器逃逸。
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "SaveArchiveFailed"), ex.Message));
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, ex.Message, LanguageControl.Ok, null, null));
                return;
            }
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    LanguageControl.Get("RemoteImportManager", "ImportComplete"),
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ZIPModpackHasBeenSavedLocally"), fileName),
                    LanguageControl.Ok,
                    null,
                    null));
        }

        // 清单链导入流程（2026-09-29 用户裁定重排）：内存下完 → 同名整合包覆盖确认
        // → 清单直接落盘 → ConfirmDownloadAndEnable 是/否问句（DFM 同款三态子项清单）。
        // 旧「开始下载/仅导入清单」三钮框删除：清单在提问前已落盘，答"否"即旧
        // 「仅导入清单」语义（管理屏可补装或删除），与 DFM 取消路径行为对齐。
        private static void ConfirmRemoteModpackOverwrite(int requestId, ModpackManifest manifest, string url, string fileName, IReadOnlyDictionary<string, byte[]> embeddedFiles, byte[] sourceData)
        {
            string conflictingPath = ModpackRegistry.Shared.FindConflictingPackPath(fileName);
            if (conflictingPath != null && s_showDuplicatePrompt)
            {
                string message = string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("RemoteImportManager", "LocalAlreadyExistsOverwriteIt"),
                    LanguageControl.Get("RemoteImportManager", "Modpack"),
                    Storage.GetFileName(conflictingPath));
                DialogsManager.ShowDialog(
                    null,
                    new CommunityImportOverwriteDialog(message, canRename: false, action =>
                    {
                        if (action == ImportOverwriteAction.Overwrite)
                        {
                            SaveRemoteModpackAndAskEnable(requestId, manifest, url, fileName, embeddedFiles, sourceData, overwriteConflicting: true);
                        }
                        // 取消：一个字节未落盘，零残留。
                    }));
                return;
            }

            SaveRemoteModpackAndAskEnable(requestId, manifest, url, fileName, embeddedFiles, sourceData, overwriteConflicting: false);
        }

        private static void SaveRemoteModpackAndAskEnable(int requestId, ModpackManifest manifest, string url, string fileName, IReadOnlyDictionary<string, byte[]> embeddedFiles, byte[] sourceData, bool overwriteConflicting)
        {
            // 覆盖确认框可能悬置到下一次导入之后才被点击（帧首投递显示、同帧任务即释放，
            // 下一帧起 IsBusy 已 false）。过期 requestId 或已有导入在跑时不再落盘/提问。
            if (requestId != s_prepareRequestId || s_importTask != null)
            {
                return;
            }
            if (!TrySaveManifest(manifest, url, fileName, sourceData, overwriteConflicting))
            {
                return;
            }

            ModpackPresenceSnapshot snapshot = new ModpackPresenceManager().Capture(null);
            string body = DownloadFlowManager.BuildModpackConfirmBody(manifest, embeddedFiles, snapshot, out _);
            string title = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : fileName;
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    title,
                    body,
                    LanguageControl.Get("DownloadFlowManager", "Yes"),
                    LanguageControl.Get("DownloadFlowManager", "No"),
                    button =>
                    {
                        if (button == MessageDialogButton.Button1)
                        {
                            // 同款复检：防旧包以过期 requestId 启动产生「0 项完成」误导弹窗。
                            if (requestId != s_prepareRequestId || s_importTask != null)
                            {
                                return;
                            }
                            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "UserConfirmedModpackImport"), manifest.Items.Length));
                            s_progress = new CommunityTransferProgress();
                            s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("RemoteImportManager", "Modpack"), progress: s_progress);
                            s_downloadDialog.SetProgress(s_progress);
                            DialogsManager.ShowDialog(null, s_downloadDialog);
                            s_importTask = Task.Run(() => ProcessRemoteModpackItemsAsync(requestId, manifest, url, fileName, embeddedFiles, sourceData));
                            AttachImportCompletion(s_importTask, requestId);
                        }
                        // "否"：清单已落盘（等于旧"仅导入清单"），静默关闭。
                    }));
        }

        private static async Task<CommunityFileDownloadResult> ProcessRemoteModpackItemsAsync(int requestId, ModpackManifest manifest, string sourceUrl, string sourceFileName, IReadOnlyDictionary<string, byte[]> embeddedFiles, byte[] sourceData)
        {
            try
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "StartProcessingModpackItemsWithTotal"), manifest.Items.Length));
                RemoteModpackResult workflowResult = await s_modpackInstaller.ExecuteAsync(
                    requestId,
                    manifest,
                    embeddedFiles,
                    GetConfiguredApiBaseUrl(),
                    null);
                int successCount = workflowResult.SuccessCount;
                int failedCount = workflowResult.FailedCount;
                int skippedCount = workflowResult.SkippedCount;
                string lastError = workflowResult.LastError;
                if (workflowResult.Interrupted)
                {
                    return FinishInterruptedRemoteModpackDownload(manifest);
                }
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackProcessingCompleted"), successCount, failedCount));
                ModpackRegistry.Shared.SaveManifest(manifest, sourceUrl, sourceFileName, sourceData);

                string resultMessage = failedCount > 0
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        successCount > 0
                            ? LanguageControl.Get("RemoteImportManager", "DownloadPackPartialFailure")
                            : LanguageControl.Get("RemoteImportManager", "DownloadPackFailure"),
                        successCount,
                        failedCount)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        skippedCount > 0
                            ? LanguageControl.Get("RemoteImportManager", "DownloadPackCompleteWithSkipped")
                            : LanguageControl.Get("RemoteImportManager", "DownloadPackComplete"),
                        successCount,
                        failedCount,
                        skippedCount);

                CommunityFileDownloadResult result = failedCount > 0
                    ? new CommunityFileDownloadResult
                    {
                        FileName = manifest.Name ?? manifest.PackageName,
                        Error = string.IsNullOrWhiteSpace(lastError)
                            ? resultMessage
                            : $"{resultMessage}\n\n{lastError}"
                    }
                    : new CommunityFileDownloadResult { FileName = manifest.Name ?? manifest.PackageName, ImportedName = resultMessage, HideProgressDialog = true };
                s_progress.Set(resultMessage, 1f, false);
                return result;
            }
            catch (Exception ex)
            {
                // 顶层兜底：任何未预期异常（磁盘满写清单、工作流内部故障等）转失败结果，
                // 任务不再 Fault，完成回调可以统一展示失败。
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "ModpackImportFailed"), ex));
                return new CommunityFileDownloadResult { FileName = manifest?.Name ?? manifest?.PackageName, Error = ex.Message };
            }
        }

        private static string GetConfiguredApiBaseUrl()
        {
            // 设置为静态存储可直读，但 ApiBaseUrl 的权威写侧在主线程（屏 Enter/设置屏）：
            // 编组回游戏线程读保证与写侧同线程（本任务运行在 Task.Run）。
            // wait:true 仅在主线程不会同步等待本任务结果时安全；否则与主线程互等死锁（PlayedTimeTracker 教训）。
            string apiBaseUrl = null;
            Dispatcher.Dispatch(() =>
            {
                apiBaseUrl = CommunityServerManager.CurrentApiBaseUrl;
            }, waitUntilCompleted: true);
            return apiBaseUrl;
        }

        // 保存清单（确认按钮回调与导入任务内均有磁盘 I/O）：失败时弹错误框并返回 false，
        // 调用方中止后续流程——磁盘满等异常不允许从对话框按钮处理器逃逸。
        private static bool TrySaveManifest(ModpackManifest manifest, string url, string fileName, byte[] sourceData, bool overwriteConflicting)
        {
            try
            {
                ModpackRegistry.Shared.SaveManifest(manifest, url, fileName, sourceData, overwriteConflicting);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "SaveModpackManifestFailed"), ex.Message));
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, ex.Message, LanguageControl.Ok, null, null));
                return false;
            }
        }

        private static CommunityFileDownloadResult FinishInterruptedRemoteModpackDownload(ModpackManifest manifest)
        {
            string message = LanguageControl.Get("RemoteImportManager", "DownloadInterruptedManifestKept");
            s_progress.Set(message, 1f, false);
            return new CommunityFileDownloadResult
            {
                FileName = manifest.Name ?? manifest.PackageName,
                ImportedName = message,
                Canceled = true
            };
        }

    }
}
