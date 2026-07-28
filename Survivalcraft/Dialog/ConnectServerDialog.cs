using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using Engine;


namespace Game {
    public class ConnectServerDialog : Dialog {
        private TextBoxWidget m_ipTextBoxWidget;

        private ButtonWidget m_connectButtonWidget;

        private ButtonWidget m_cancelButtonWidget;
        private bool m_isResolvingAddress;

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
            if (Input.Cancel && !m_isResolvingAddress) {
                Dismiss();
            }
            else if (Input.Ok) {
                ConnectToServer();
            }
            else if (m_connectButtonWidget.IsClicked) {
                ConnectToServer();
            }
            else if (m_cancelButtonWidget.IsClicked && !m_isResolvingAddress) {
                Dismiss();
            }
        }

        public void ConnectToServer() {
            if (m_isResolvingAddress) return;
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
                if (relayParts.Length != 2
                    || string.IsNullOrWhiteSpace(relayParts[1])
                    || !TryParseEndPointParts(relayParts[0], 28888, out EndPointParts relayEndPointParts)) {
                    DialogsManager.ShowDialog(null, new MessageDialog("错误", "中转地址格式应为 relay@地址[:端口]#房间码，IPv6 地址请使用 [::1]:端口", LanguageControl.Ok, null, null));
                    return;
                }
                ResolveAndConnectEndPoint(relayEndPointParts, endPoint => new RelayJoinInfo(endPoint, relayParts[1].Trim()), "中转地址解析失败");
                return;
            }

            if (LooksLikeGlobalCode(ipAddress)) {
                ResolveAndConnectGlobalCode(ipAddress);
                return;
            }

            if (!TryParseEndPointParts(ipAddress, 28887, out EndPointParts endPointParts)) {
                DialogsManager.ShowDialog(null, new MessageDialog(
                    "错误",
                    "服务器地址格式应为 地址[:端口]，IPv6 地址请使用 [::1]:端口",
                    LanguageControl.Ok, null, null));
                return;
            }

            ResolveAndConnectEndPoint(endPointParts, endPoint => endPoint, "服务器地址解析失败");
        }

        private static bool TryParseEndPointParts(string text, int defaultPort, out EndPointParts endPointParts) {
            endPointParts = default;
            text = text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            string host;
            int port = defaultPort;

            if (text[0] == '[') {
                int closeBracketIndex = text.IndexOf(']');
                if (closeBracketIndex <= 1) return false;
                host = text[1..closeBracketIndex];
                string suffix = text[(closeBracketIndex + 1)..];
                if (suffix.Length > 0) {
                    if (suffix[0] != ':' || !TryParsePort(suffix[1..], out port)) return false;
                }
            }
            else {
                int firstColonIndex = text.IndexOf(':');
                int lastColonIndex = text.LastIndexOf(':');
                if (firstColonIndex >= 0 && firstColonIndex == lastColonIndex) {
                    host = text[..firstColonIndex];
                    if (!TryParsePort(text[(firstColonIndex + 1)..], out port)) return false;
                }
                else {
                    host = text;
                }
            }

            if (string.IsNullOrWhiteSpace(host)) return false;
            endPointParts = new EndPointParts(host.Trim(), port);
            return true;
        }

        private static bool TryParsePort(string text, out int port) {
            return int.TryParse(text, out port) && port > 0 && port <= 65535;
        }

        private void ResolveAndConnectEndPoint(EndPointParts endPointParts, Func<IPEndPoint, object> createGameLoadingParameter, string failureMessagePrefix) {
            m_isResolvingAddress = true;
            m_connectButtonWidget.IsEnabled = false;
            Task.Run(async () => {
                try {
                    IPAddress address = await NetworkManager.TryGetIPAddress(endPointParts.Host);
                    if (address == null) {
                        throw new InvalidOperationException("未找到可用地址");
                    }
                    IPEndPoint endPoint = new(address, endPointParts.Port);
                    Dispatcher.Dispatch(delegate {
                        m_isResolvingAddress = false;
                        m_connectButtonWidget.IsEnabled = true;
                        ScreensManager.SwitchScreen("GameLoading", null, null, createGameLoadingParameter(endPoint), null);
                        Dismiss();
                    });
                }
                catch (Exception ex) {
                    Dispatcher.Dispatch(delegate {
                        m_isResolvingAddress = false;
                        m_connectButtonWidget.IsEnabled = true;
                        DialogsManager.ShowDialog(null, new MessageDialog("错误", $"{failureMessagePrefix}：{ex.Message}", LanguageControl.Ok, null, null));
                    });
                }
            });
        }

        private void ResolveAndConnectGlobalCode(string globalCode) {
            m_isResolvingAddress = true;
            m_connectButtonWidget.IsEnabled = false;
            Task.Run(async () => {
                try {
                    RelayJoinInfo joinInfo = await NetworkManager.ResolveRelayJoinInfoAsync(globalCode);
                    Dispatcher.Dispatch(delegate {
                        m_isResolvingAddress = false;
                        m_connectButtonWidget.IsEnabled = true;
                        ScreensManager.SwitchScreen("GameLoading", null, null, joinInfo, null);
                        Dismiss();
                    });
                }
                catch (Exception ex) {
                    Dispatcher.Dispatch(delegate {
                        m_isResolvingAddress = false;
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

        private readonly struct EndPointParts {
            public EndPointParts(string host, int port) {
                Host = host;
                Port = port;
            }

            public string Host { get; }

            public int Port { get; }
        }

        public void Dismiss() {
            if (AutoHide) {
                DialogsManager.HideDialog(this);
            }
        }
    }
}


