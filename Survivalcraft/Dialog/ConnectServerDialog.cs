using System.Net;
using System.Xml.Linq;
using Game.Network;

namespace Game {
    public class ConnectServerDialog : Dialog {
        private LabelWidget m_titleWidget;

        private LabelWidget m_ipLabelWidget;

        private TextBoxWidget m_ipTextBoxWidget;

        private ButtonWidget m_connectButtonWidget;

        private ButtonWidget m_cancelButtonWidget;

        public bool AutoHide {
            get;
            set;
        }

        public ConnectServerDialog() {
            XElement node = ContentManager.Get<XElement>("Dialogs/ConnectServerDialog");
            LoadContents(this, node);
            m_titleWidget = Children.Find<LabelWidget>("ConnectServerDialog.Title");
            m_ipLabelWidget = Children.Find<LabelWidget>("ConnectServerDialog.IpLabel");
            m_ipTextBoxWidget = Children.Find<TextBoxWidget>("ConnectServerDialog.IpTextBox");
            m_connectButtonWidget = Children.Find<ButtonWidget>("ConnectServerDialog.ConnectButton");
            m_cancelButtonWidget = Children.Find<ButtonWidget>("ConnectServerDialog.CancelButton");

            // 设置默认IP地址
            m_ipTextBoxWidget.Text = "127.0.0.1:28887";
            m_ipTextBoxWidget.HasFocus = true;
            m_ipTextBoxWidget.Enter += delegate {
                ConnectToServer();
            };

            AutoHide = true;
        }

        public override void Update() {
            if (Input.Cancel) {
                Dismiss();
            }
            else if (Input.Ok) {
                ConnectToServer();
            }
            else if (m_connectButtonWidget.IsClicked) {
                ConnectToServer();
            }
            else if (m_cancelButtonWidget.IsClicked) {
                Dismiss();
            }
        }

        public void ConnectToServer() {
            string ipAddress = m_ipTextBoxWidget.Text?.Trim();

            if (string.IsNullOrEmpty(ipAddress)) {
                DialogsManager.ShowDialog(null, new MessageDialog(
                    "错误",
                    "请输入服务器地址",
                    LanguageControl.Ok, null, null));
                return;
            }

            // 解析IP和端口
            string[] parts = ipAddress.Split(':');
            string ip = parts[0];
            int port = 28887; // 默认端口

            if (parts.Length > 1) {
                if (!(int.TryParse(parts[1], out port) && NetworkHelper.IsValidPort(port))) {
                    DialogsManager.ShowDialog(null, new MessageDialog(
                        "错误",
                        "无效的端口号",
                        LanguageControl.Ok, null, null));
                    return;
                }
            }

            if (!NetworkHelper.IsValidIPAddress(ip)) {
                DialogsManager.ShowDialog(null, new MessageDialog(
                    "错误",
                    "无效的IP地址",
                    LanguageControl.Ok, null, null));
                return;
            }

            ScreensManager.SwitchScreen("GameLoading", null, null, IPEndPoint.Parse(ip + ":" + port), null);
            Dismiss();
        }

        public void Dismiss() {
            if (AutoHide) {
                DialogsManager.HideDialog(this);
            }
        }
    }
}


