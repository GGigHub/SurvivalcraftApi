using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using Engine;
using Game.Network;


namespace Game {
    public class ConnectServerDialog : Dialog {
        private TextBoxWidget m_ipTextBoxWidget;

        private ButtonWidget m_connectButtonWidget;

        private ButtonWidget m_cancelButtonWidget;
        private bool m_isResolvingGlobalCode;

        public bool AutoHide {
            get;
            set;
        }

        public ConnectServerDialog() {
            XElement node = ContentManager.Get<XElement>("Dialogs/ConnectServerDialog");
            LoadContents(this, node);
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
            if (m_isResolvingGlobalCode) return;
            string ipAddress = m_ipTextBoxWidget.Text?.Trim();

            if (string.IsNullOrEmpty(ipAddress)) {
                DialogsManager.ShowDialog(null, new MessageDialog(
                    "错误",
                    "请输入服务器地址",
                    LanguageControl.Ok, null, null));
                return;
            }

            // 中转格式：relay@IP:端口#房间码
            if (ipAddress.StartsWith("relay@", StringComparison.OrdinalIgnoreCase)) {
                string[] relayParts = ipAddress[6..].Split('#', 2);
                if (relayParts.Length != 2 || string.IsNullOrWhiteSpace(relayParts[1]) || !TryParseEndPoint(relayParts[0], 28888, out IPEndPoint relayEndPoint)) {
                    DialogsManager.ShowDialog(null, new MessageDialog("错误", "中转地址格式应为 relay@IP:端口#房间码", LanguageControl.Ok, null, null));
                    return;
                }
                ScreensManager.SwitchScreen("GameLoading", null, null, new RelayJoinInfo(relayEndPoint, relayParts[1]), null);
                Dismiss();
                return;
            }

            if (LooksLikeGlobalCode(ipAddress)) {
                ResolveAndConnectGlobalCode(ipAddress);
                return;
            }

            // 解析IP和端口
            string[] parts = ipAddress.Split(':');
            string ip = parts[0];
            int port = 28887; // 默认端口

            if (parts.Length > 1) {
                if (!(int.TryParse(parts[1], out port) && port > 0 && port <= 65535)) {
                    DialogsManager.ShowDialog(null, new MessageDialog(
                        "错误",
                        "无效的端口号",
                        LanguageControl.Ok, null, null));
                    return;
                }
            }

            if (!IPAddress.TryParse(ip, out _)) {
                DialogsManager.ShowDialog(null, new MessageDialog(
                    "错误",
                    "无效的IP地址",
                    LanguageControl.Ok, null, null));
                return;
            }

            ScreensManager.SwitchScreen("GameLoading", null, null, IPEndPoint.Parse(ip + ":" + port), null);
            Dismiss();
        }

        private static bool TryParseEndPoint(string text, int defaultPort, out IPEndPoint endPoint) {
            endPoint = null;
            string[] parts = text.Split(':');
            int port = defaultPort;
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out IPAddress address)) return false;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out port) || port < 1 || port > 65535)) return false;
            endPoint = new IPEndPoint(address, port);
            return true;
        }

        private void ResolveAndConnectGlobalCode(string globalCode) {
            m_isResolvingGlobalCode = true;
            m_connectButtonWidget.IsEnabled = false;
            Task.Run(async () => {
                try {
                    RelayJoinInfo joinInfo = await CenterServerClient.ResolveRelayJoinInfoAsync(globalCode);
                    Dispatcher.Dispatch(delegate {
                        m_isResolvingGlobalCode = false;
                        m_connectButtonWidget.IsEnabled = true;
                        ScreensManager.SwitchScreen("GameLoading", null, null, joinInfo, null);
                        Dismiss();
                    });
                }
                catch (Exception ex) {
                    Dispatcher.Dispatch(delegate {
                        m_isResolvingGlobalCode = false;
                        m_connectButtonWidget.IsEnabled = true;
                        DialogsManager.ShowDialog(null, new MessageDialog("错误", $"联机码解析失败：{ex.Message}", LanguageControl.Ok, null, null));
                    });
                }
            });
        }

        private static bool LooksLikeGlobalCode(string text) {
            if (string.IsNullOrWhiteSpace(text) || text.Contains(':') || text.Contains('@') || text.Contains('/') || text.Contains('.')) return false;
            int count = 0;
            foreach (char c in text) {
                if (c is '-' or ' ') continue;
                if (!char.IsDigit(c)) return false;
                count++;
            }
            return count == 4;
        }

        public void Dismiss() {
            if (AutoHide) {
                DialogsManager.HideDialog(this);
            }
        }
    }
}


