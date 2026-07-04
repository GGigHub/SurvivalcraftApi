using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using Engine;

namespace Game {
    public class HotUpdateDialog : Dialog {
        public LabelWidget m_largeLabelWidget;
        public LabelWidget m_smallLabelWidget;

        private bool m_downloadStarted = false;
        private bool m_restartRequested = false;
        private Task m_downloadTask = null;

        public HotUpdateDialog() {
            XElement node = ContentManager.Get<XElement>("Dialogs/BusyDialog");
            LoadContents(this, node);
            m_largeLabelWidget = Children.Find<LabelWidget>("BusyDialog.LargeLabel");
            m_smallLabelWidget = Children.Find<LabelWidget>("BusyDialog.SmallLabel");
            
            m_largeLabelWidget.Text = "正在下载更新";
            m_smallLabelWidget.Text = "请稍候...";
        }

        public override void Update() {
            if (!m_downloadStarted) {
                m_downloadStarted = true;
                m_downloadTask = HotUpdateManager.DownloadAndInstallUpdateAsync();
            }

            if (Input.Back) {
                Input.Clear();
                // 不允许取消下载，拦截返回键
            }

            if (HotUpdateManager.State == HotUpdateState.Downloading) {
                float progress = HotUpdateManager.DownloadProgress * 100f;
                m_smallLabelWidget.Text = $"进度: {progress:0.1}%";
            }
            else if (HotUpdateManager.State == HotUpdateState.ReadyToRestart) {
                m_smallLabelWidget.Text = "更新完成，正在重启...";
                // windows平台由脚本控制重启
                if (!m_restartRequested) {
                    m_restartRequested = true;
                    HotUpdateManager.Restart();
                }
            }
            else if (HotUpdateManager.State == HotUpdateState.Error) {
                DialogsManager.HideDialog(this);
                DialogsManager.ShowDialog(null, new MessageDialog("更新失败", HotUpdateManager.LastError?.Message, "确定", null, null));
            }
        }
    }
}
