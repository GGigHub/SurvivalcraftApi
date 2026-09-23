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
        /// <summary>每帧轮询：进度对话框、下载队列推进、三个后台任务的完成收尾。由全局帧泵（CommunityFramePump）驱动，离屏也持续收尾。</summary>
        public void Update()
        {
            UpdateFileDownloadDialog();
            ReshowLostQueuedOverwriteDialog();
            StartNextQueuedDownload();

            if (CompleteFileDownloadIfFinished())
            {
                return;
            }
            CompleteModpackManifestLoadIfFinished();
            CompleteModpackItemsDownloadIfFinished();
        }

        /// <summary>文件下载任务收尾。返回 true 表示本轮终止（重排队确认/暂停/取消分支不继续后续段）。</summary>
        private bool CompleteFileDownloadIfFinished()
        {
            if (m_fileDownloadTask == null || !m_fileDownloadTask.IsCompleted)
            {
                return false;
            }
            CommunityFileDownloadResult result;
            try
            {
                result = m_fileDownloadTask.Result;
            }
            catch (Exception ex)
            {
                // 任务 Faulted（如导入器抛未预期异常）：不冒泡出 Update()，按下载失败路径展示。
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadTaskFaulted"), ex.Message));
                result = new CommunityFileDownloadResult
                {
                    FileName = m_currentDownloadItem?.FileName,
                    DownloadFailed = true,
                    Error = ex.Message
                };
            }
            m_fileDownloadTask = null;
            CommunityDownloadQueueItem completedDownloadItem = m_currentDownloadItem;
            bool wasQueuedDownload = completedDownloadItem != null;
            if (CompleteCurrentDownloadItem(result))
            {
                // 已就「文件已存在」弹覆盖确认并重排队：错误属预期，失败 UI 会叠出第二个对话框。
                return true;
            }
                if (result?.Paused == true)
                {
                    string message = result.Error ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "DownloadPaused"), result.FileName);
                    ReportStatus(message);
                    m_fileDownloadDialog?.SetStopped(message);
                    StartNextQueuedDownload();
                    return true;
                }
                if (result?.Canceled == true)
                {
                    string message = result.Error ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "DownloadCanceled"), result.FileName);
                    ReportStatus(message);
                    m_fileDownloadDialog?.SetStopped(message);
                    StartNextQueuedDownload();
                    return true;
                }
                if (string.IsNullOrEmpty(result.Error))
                {
                    if (result.Type == ExternalContentType.Mod && result.PostId > 0)
                    {
                        LocalModStateStore.RememberLocalModPostId(result.ImportedName, result.FileName, result.PostId);
                    }
                    DownloadCompleted?.Invoke(result);
                    ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "Imported"), result.FileName));
                    m_fileDownloadDialog?.SetCompleted(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "Imported"), result.FileName));
                    ShowToast(
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "DownloadComplete"), result.FileName),
                        autoHideSeconds: 2.4);
                    if (result.RequiresRestart)
                    {
                        RestartConfirmDialog.ShowModImportRestartConfirm(result.FileName, "import");
                    }
                }
                else
                {
                    bool useDownloadFailureText = wasQueuedDownload && result.DownloadFailed;
                    string failureMessage = string.Format(
                        CultureInfo.InvariantCulture,
                        useDownloadFailureText
                            ? LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "DownloadFailed")
                            : LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "ImportFailed"),
                        result.Error);
                    ReportStatus(failureMessage);
                    m_fileDownloadDialog?.SetFailed(result.Error);
                    if (useDownloadFailureText)
                    {
                        ShowDownloadFailureDialog(completedDownloadItem);
                    }
                    else
                    {
                        DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, result.Error, LanguageControl.Ok, null, null));
                    }
                }
                StartNextQueuedDownload();
            return false;
        }

        private void CompleteModpackManifestLoadIfFinished()
        {
            if (m_modpackManifestLoadTask == null || !m_modpackManifestLoadTask.IsCompleted)
            {
                return;
            }
            ModpackManifestLoadResult manifestResult;
            try
            {
                manifestResult = m_modpackManifestLoadTask.Result;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "BackgroundManifestLoadTaskFaulted"), ex.Message));
                manifestResult = null;
            }
            m_modpackManifestLoadTask = null;
            if (manifestResult != null && manifestResult.RequestId == m_modpackDownloadRequestId)
            {
                if (!string.IsNullOrWhiteSpace(manifestResult.Error))
                {
                    ShowToast(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "FailedToLoadModpackManifest"), manifestResult.Error));
                }
                else
                {
                    ConfirmAndProcessModpack(manifestResult);
                }
            }
        }

        private void CompleteModpackItemsDownloadIfFinished()
        {
            if (m_modpackItemDownloadTask == null || !m_modpackItemDownloadTask.IsCompleted)
            {
                return;
            }
            ModpackDownloadProgress progress;
            try
            {
                progress = m_modpackItemDownloadTask.Result;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "BackgroundItemDownloadTaskFaulted"), ex.Message));
                progress = new ModpackDownloadProgress { Error = ex.Message, Results = [] };
            }
            m_modpackItemDownloadTask = null;
            Action<ModpackDownloadProgress> completedAction = m_modpackDownloadCompletedAction;
            m_modpackDownloadCompletedAction = null;
            bool allowStartAfterDownloadFailures = m_modpackAllowStartAfterDownloadFailures;
            m_modpackAllowStartAfterDownloadFailures = false;
            bool hasFailures = progress.FailedCount > 0 || !string.IsNullOrEmpty(progress.Error) || progress.Interrupted;
            string summary = hasFailures
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    progress.SuccessCount > 0
                        ? LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "DownloadPackPartialFailure")
                        : LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "DownloadPackFailure"),
                    progress.SuccessCount,
                    progress.FailedCount)
                : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "ModpackDownloadFinishedSummary"),
                    progress.SuccessCount,
                    progress.FailedCount);
            if (!string.IsNullOrEmpty(progress.Error))
            {
                summary = string.IsNullOrEmpty(summary) ? progress.Error : summary + "：" + progress.Error;
            }
            if (progress.SkippedCount > 0)
            {
                // 缺陷 1 汇总：撞本地同名的跳过项单独提示（不计成功/失败）。
                summary += string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "SkippedLocalDuplicates"),
                    progress.SkippedCount);
            }
            ReportStatus(summary);
            ShowToast(summary, autoHideSeconds: 5.0);
            if (hasFailures && !allowStartAfterDownloadFailures)
            {
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Error,
                        BuildModpackDownloadFailureMessage(progress, summary),
                        LanguageControl.Ok,
                        null,
                        null));
            }
            PackDownloadCompleted?.Invoke();
            completedAction?.Invoke(progress);
        }
    }
}