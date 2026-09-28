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
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "Downloading"),
                        item.Filename ?? item.FileId ?? item.Url,
                        itemIndex,
                        totalCount),
                    (float)(itemIndex - 1) / totalCount,
                    true),
                item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "HasNoDownloadLinkSkipped"), item.Filename),
                item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "UnknownItemCategory"), item.Category),
                item => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "DataFileHostUnavailable"), item.Filename));

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
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ShowConflictDialogFailed"), ex));
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
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "RemoteImportPrepareTaskFaulted"), ex));
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
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportComplete"),
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ZIPModpackHasBeenSavedLocally"), result.FileName),
                        LanguageControl.Ok,
                        null,
                        null));
                return;
            }

            if (result.Manifest != null)
            {
                if (s_downloadDialog != null)
                {
                    DialogsManager.HideDialog(s_downloadDialog);
                    s_downloadDialog = null;
                }
                ConfirmAndStartRemoteModpackDownload(requestId, result.Manifest, result.Url, result.FileName, result.EmbeddedFiles, result.OriginalData);
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
                string overwriteMessage = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "LocalAlreadyExistsOverwriteIt"), resourceTypeName, existingName);
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
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "RemoteImportTaskFaulted"), ex));
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
                    DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("ScbbsPlusRemoteImportManager", "Modpack"), result.ImportedName ?? LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadInterruptedManifestKept"), LanguageControl.Ok, null, null));
                    return;
                }

                s_progress?.Set(result.ImportedName ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "Imported"), result.FileName), 1f, false);
                if (result.HideProgressDialog)
                {
                    HideRemoteImportDialog();
                }
                else
                {
                    s_downloadDialog?.SetCompleted(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "Imported"), result.FileName));
                }
                if (result.RequiresRestart)
                {
                    RestartConfirmDialog.ShowModImportRestartConfirm(result.FileName, "remote-import");
                }
                else
                {
                    DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadComplete"), string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "Imported"), result.FileName), LanguageControl.Ok, null, null));
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
            Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "RemoteImportCompletionFaulted"), exception));
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
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportOrDownloadBusy"), LanguageControl.Ok, null, null));
                return;
            }

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
                s_showDuplicatePrompt = showDuplicatePrompt;
                s_progress = new CommunityTransferProgress();
                s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportResource"), progress: s_progress);
                s_downloadDialog.SetProgress(s_progress);
                DialogsManager.ShowDialog(null, s_downloadDialog);
                s_prepareTask = Task.Run(async () =>
                {
                    try
                    {
                        string serverFileName = null;
                        using MemoryStream stream = await DownloadToMemoryAsync(request.Url, request.FileName, s_progress, name => serverFileName = name);
                        s_progress.Set(LanguageControl.Get("ScbbsPlusRemoteImportManager", "CheckingLocalResources"), 1f, false);
                        ApplyServerFileName(request, serverFileName);
                        if (request.Type == ExternalContentType.Unknown)
                        {
                            return new ExternalContentPreparation
                            {
                                RequestId = requestId,
                                FileName = request.FileName,
                                Type = request.Type,
                                Error = LanguageControl.Get("ScbbsPlusRemoteImportManager", "UnsupportedFileExtension")
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
                s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportResource"), progress: s_progress);
                s_downloadDialog.SetProgress(s_progress);
                DialogsManager.ShowDialog(null, s_downloadDialog);
            }

            s_progress?.Set(LanguageControl.Get("ScbbsPlusRemoteImportManager", "Importing"), 1f, false);
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
            progress?.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadingFormat"), fileName), 0f, true);
            Progress<DownloadProgress> byteProgress = new(info =>
            {
                if (info.TotalBytes is > 0)
                {
                    progress?.Set(
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadingFormat"), fileName),
                        info.BytesDownloaded / (float)info.TotalBytes.Value,
                        true);
                }
                else
                {
                    progress?.Set(LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadingProgress"), 0f, false);
                }
            });
            try
            {
                return await s_httpDownloader.DownloadToMemoryAsync(url, null, CancellationToken.None, byteProgress, serverFileName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadFailedFormat"), ex.Message), ex);
            }
        }

        private static void StartRemoteModpackImport(string url, string fileName)
        {
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "StartModpackImport"), url, fileName));
            bool downloadArchiveOnly = string.Equals(Storage.GetExtensionBCL(fileName), ".zip", StringComparison.OrdinalIgnoreCase);

            s_progress = new CommunityTransferProgress();
            s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportModpack"), progress: s_progress);
            s_downloadDialog.SetProgress(s_progress);
            DialogsManager.ShowDialog(null, s_downloadDialog);

            int requestId = ++s_prepareRequestId;
            s_prepareTask = Task.Run(async () =>
            {
                string kindWord = downloadArchiveOnly ? string.Empty : LanguageControl.Get("ScbbsPlusRemoteImportManager", "Manifest");
                try
                {
                    s_progress.Set(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            downloadArchiveOnly
                                ? LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadArchiveFormat")
                                : LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadManifestFormat"),
                            fileName),
                        0f,
                        true);
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadModpackPackage"), kindWord, url));
                    Progress<DownloadProgress> byteProgress = new(info =>
                    {
                        if (info.TotalBytes is > 0)
                        {
                            s_progress.Set(
                                string.Format(CultureInfo.InvariantCulture,
                                    downloadArchiveOnly
                                        ? LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadArchiveFormat")
                                        : LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadManifestFormat"),
                                    fileName),
                                info.BytesDownloaded / (float)info.TotalBytes.Value,
                                true);
                        }
                    });
                    using MemoryStream dataStream = await s_httpDownloader.DownloadToMemoryAsync(url, null, CancellationToken.None, byteProgress);
                    byte[] data = dataStream.ToArray();
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackPackageDownloadCompleted"), kindWord, data.Length));
                    if (downloadArchiveOnly)
                    {
                        ModpackRegistry.Shared.SaveArchive(data, url, fileName);
                        return new ExternalContentPreparation { RequestId = requestId, FileName = fileName, ArchiveSaved = true };
                    }

                    ModpackManifestLoadResult manifestResult = s_manifestParser.Parse(requestId, data, url, fileName);
                    ModpackManifest manifest = manifestResult.Manifest;

                    if (manifest == null || manifest.Items == null || manifest.Items.Length == 0)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackManifestEmptyOrInvalid"), url));
                        return new ExternalContentPreparation
                        {
                            RequestId = requestId,
                            FileName = fileName,
                            Error = manifestResult.Error ?? LanguageControl.Get("ScbbsPlusRemoteImportManager", "TheModpackManifestIsEmptyOrInvalid")
                        };
                    }

                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackSummary"), manifest.Name, manifest.PackageName, manifest.Version, manifest.Items.Length));
                    for (int i = 0; i < manifest.Items.Length; i++)
                    {
                        ModpackItem item = manifest.Items[i];
                        Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackItemDetails"), i, item.TargetDirectory, item.Filename, item.Url, item.FileId, item.Category, item.TargetPath));
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
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackManifestDownloadFailed"), url, ex));
                    return new ExternalContentPreparation { RequestId = requestId, FileName = fileName, Error = ex.Message };
                }
            });
            AttachPrepareCompletion(s_prepareTask, requestId);
        }

        private static void ConfirmAndStartRemoteModpackDownload(int requestId, ModpackManifest manifest, string url, string fileName, IReadOnlyDictionary<string, byte[]> embeddedFiles, byte[] sourceData)
        {
            int totalCount = manifest.Items.Length;
            string title = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : fileName;
            bool canImportManifestOnly = string.Equals(Storage.GetExtensionBCL(fileName), ".json", StringComparison.OrdinalIgnoreCase);
            string actionPrompt = canImportManifestOnly
                ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "SubitemsCountPrompt"), totalCount)
                : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "TotalItemsDownloadAll"), totalCount);
            string body = string.IsNullOrWhiteSpace(manifest.Description)
                ? actionPrompt
                : manifest.Description + "\n\n" + actionPrompt;

            DialogsManager.ShowDialog(null,
                new MessageDialog(
                    title,
                    body,
                    LanguageControl.Get("ScbbsPlusRemoteImportManager", "StartDownload"),
                    canImportManifestOnly
                        ? LanguageControl.Get("ScbbsPlusRemoteImportManager", "OnlyImportManifest")
                        : LanguageControl.Cancel,
                    button =>
                    {
                        if (button == MessageDialogButton.Button1)
                        {
                            // 复检：确认对话框可能悬置到下一次导入之后才被点击（帧首投递显示、
                            // 同帧任务即释放，下一帧起 IsBusy 已 false）。过期 requestId 或已有
                            // 导入在跑时忽略本次确认，防旧包以过期 requestId 启动产生
                            // 「0 项完成」误导弹窗与无谓 SaveManifest。
                            if (requestId != s_prepareRequestId || s_importTask != null)
                            {
                                return;
                            }
                            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "UserConfirmedModpackImport"), totalCount));
                            if (!TrySaveManifest(manifest, url, fileName, sourceData))
                            {
                                return;
                            }
                            s_progress = new CommunityTransferProgress();
                            s_downloadDialog = new CommunityDownloadDialog(LanguageControl.Get("ScbbsPlusRemoteImportManager", "Modpack"), progress: s_progress);
                            s_downloadDialog.SetProgress(s_progress);
                            DialogsManager.ShowDialog(null, s_downloadDialog);
                            s_importTask = Task.Run(() => ProcessRemoteModpackItemsAsync(requestId, manifest, url, fileName, embeddedFiles, sourceData));
                            AttachImportCompletion(s_importTask, requestId);
                        }
                        else if (button == MessageDialogButton.Button2 && canImportManifestOnly)
                        {
                            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportModpackManifestOnly"), fileName, totalCount));
                            if (!TrySaveManifest(manifest, url, fileName, sourceData))
                            {
                                return;
                            }
                            DialogsManager.ShowDialog(
                                null,
                                new MessageDialog(
                                    LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportComplete"),
                                    LanguageControl.Get("ScbbsPlusRemoteImportManager", "ManifestSavedContentPending"),
                                    LanguageControl.Ok,
                                    null,
                                    null));
                        }
                    }));
        }

        private static async Task<CommunityFileDownloadResult> ProcessRemoteModpackItemsAsync(int requestId, ModpackManifest manifest, string sourceUrl, string sourceFileName, IReadOnlyDictionary<string, byte[]> embeddedFiles, byte[] sourceData)
        {
            try
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "StartProcessingModpackItemsWithTotal"), manifest.Items.Length));
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
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackProcessingCompleted"), successCount, failedCount));
                ModpackRegistry.Shared.SaveManifest(manifest, sourceUrl, sourceFileName, sourceData);

                string resultMessage = failedCount > 0
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        successCount > 0
                            ? LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadPackPartialFailure")
                            : LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadPackFailure"),
                        successCount,
                        failedCount)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        skippedCount > 0
                            ? LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadPackCompleteWithSkipped")
                            : LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadPackComplete"),
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
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "ModpackImportFailed"), ex));
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
        private static bool TrySaveManifest(ModpackManifest manifest, string url, string fileName, byte[] sourceData)
        {
            try
            {
                ModpackRegistry.Shared.SaveManifest(manifest, url, fileName, sourceData);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "SaveModpackManifestFailed"), ex.Message));
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, ex.Message, LanguageControl.Ok, null, null));
                return false;
            }
        }

        private static CommunityFileDownloadResult FinishInterruptedRemoteModpackDownload(ModpackManifest manifest)
        {
            string message = LanguageControl.Get("ScbbsPlusRemoteImportManager", "DownloadInterruptedManifestKept");
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
