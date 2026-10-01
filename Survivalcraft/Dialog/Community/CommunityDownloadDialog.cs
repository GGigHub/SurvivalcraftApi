using System;
using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    public sealed class CommunityDownloadDialog : Dialog
    {
        private readonly LabelWidget m_statusLabel;
        private readonly LabelWidget m_percentLabel;
        private readonly ValueBarWidget m_progressBar;
        private readonly ButtonWidget m_cancelButton;
        private readonly ButtonWidget m_backgroundButton;
        private readonly Action m_cancelAction;
        private readonly Action m_backgroundAction;
        private readonly CommunityTransferProgress m_progress;
        private bool m_closable;

        public CommunityDownloadDialog(string title, Action cancelAction = null, Action backgroundAction = null, CommunityTransferProgress progress = null)
        {
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityDownloadDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("DownloadDialog.Title").Text =
                string.IsNullOrWhiteSpace(title) ? LanguageControl.Get("DownloadDialog", "DownloadingLabel") : title;
            m_statusLabel = Children.Find<LabelWidget>("DownloadDialog.Status");
            m_percentLabel = Children.Find<LabelWidget>("DownloadDialog.Percent");
            m_progressBar = Children.Find<ValueBarWidget>("DownloadDialog.Progress");
            m_cancelButton = Children.Find<ButtonWidget>("DownloadDialog.CancelButton");
            m_backgroundButton = Children.Find<ButtonWidget>("DownloadDialog.BackgroundButton");
            m_cancelAction = cancelAction;
            m_backgroundAction = backgroundAction;
            m_progress = progress;
            SetActiveButtonsVisible(true);
        }

        public void SetProgress(CommunityTransferProgress progress)
        {
            // 进入可关闭状态（完成/失败/停止）后忽略迟到的进度事件。
            if (progress == null || m_closable)
            {
                return;
            }

            (string message, float fraction, bool hasPercent) = progress.Snapshot();
            m_statusLabel.Text = message ?? LanguageControl.Get("DownloadDialog", "DownloadingProgress");
            m_progressBar.Value = MathUtils.Saturate(fraction);
            m_percentLabel.Text = hasPercent ? $"{fraction * 100f:0}%" : "--%";
        }

        public void SetCompleted(string message)
        {
            m_statusLabel.Text = message ?? LanguageControl.Get("DownloadDialog", "DownloadComplete");
            m_progressBar.Value = 1f;
            m_percentLabel.Text = "100%";
            ShowClosableState();
        }

        public void SetFailed(string error)
        {
            m_statusLabel.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("DownloadDialog", "DownloadFailed"), error);
            ShowClosableState();
        }

        public void SetStopped(string message)
        {
            m_statusLabel.Text = message ?? LanguageControl.Get("DownloadDialog", "Canceled");
            ShowClosableState();
        }

        public override void Update()
        {
            SetProgress(m_progress);
            if (m_cancelButton.IsClicked)
            {
                m_cancelAction?.Invoke();
            }

            if (m_backgroundButton.IsClicked)
            {
                m_backgroundAction?.Invoke();
            }

            // 下载进行中不可关闭（只能取消或转后台）；完成/失败/停止后可关闭。
            if (m_closable
                && (Input.Back
                    || Input.Cancel
                    || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))))
            {
                DialogsManager.HideDialog(this);
            }
        }

        private void SetActiveButtonsVisible(bool isActive)
        {
            bool showCancelButton = isActive && m_cancelAction != null;
            bool showBackgroundButton = isActive && m_backgroundAction != null;
            m_cancelButton.IsVisible = showCancelButton;
            m_cancelButton.IsEnabled = showCancelButton;
            m_backgroundButton.IsVisible = showBackgroundButton;
            m_backgroundButton.IsEnabled = showBackgroundButton;
        }

        private void ShowClosableState()
        {
            m_cancelButton.IsVisible = false;
            m_cancelButton.IsEnabled = false;
            m_backgroundButton.IsVisible = false;
            m_backgroundButton.IsEnabled = false;
            m_closable = true;
        }
    }
}
