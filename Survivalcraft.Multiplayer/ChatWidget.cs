using Engine;
using Engine.Input;
using Game;
using Game.Network;

namespace Survivalcraft.Multiplayer {
    /// <summary>
    ///     Multiplayer chat panel. Its dimensions and placement mirror the
    ///     original game's NetMessageWidget; only plain text chat is kept.
    /// </summary>
    public class ChatWidget : CanvasWidget {
        sealed class ChatScrollPanelWidget : ScrollPanelWidget {
            public Vector2 Size { get; set; }

            public override void MeasureOverride(Vector2 parentAvailableSize) {
                base.MeasureOverride(Size);
                DesiredSize = Size;
            }
        }

        const float PanelWidth = 570f;
        const float PanelHeight = 240f;
        const float HistoryHeight = 188f;
        const float InputHeight = 48f;
        const float SendButtonSize = 60f;

        readonly CanvasWidget m_panel = new() {
            Size = new Vector2(PanelWidth, PanelHeight),
            IsHitTestVisible = true
        };
        readonly BevelledRectangleWidget m_panelBackground = new() {
            Size = new Vector2(PanelWidth, PanelHeight),
            CenterColor = new Color(0, 0, 0, 75),
            BevelColor = Color.White,
            BevelSize = 1f
        };
        readonly ChatScrollPanelWidget m_messageList = new() {
            Direction = LayoutDirection.Vertical,
            Size = new Vector2(PanelWidth - 10f, HistoryHeight),
            IsHitTestVisible = true
        };
        readonly StackPanelWidget m_history = new() {
            Direction = LayoutDirection.Vertical,
            IsHitTestVisible = false
        };
        readonly CanvasWidget m_inputArea = new() {
            Size = new Vector2(PanelWidth - SendButtonSize, InputHeight),
            IsHitTestVisible = true,
            ClampToBounds = true
        };
        readonly BevelledRectangleWidget m_inputBackground = new() {
            Size = new Vector2(PanelWidth - SendButtonSize, InputHeight),
            CenterColor = new Color(0, 0, 0, 124),
            BevelColor = Color.White,
            BevelSize = 2f
        };
        readonly TextBoxWidget m_input = new() {
            Size = new Vector2(PanelWidth - SendButtonSize, InputHeight),
            Margin = new Vector2(5f, 0f),
            FontScale = 1f,
            EnterAsNewLine = false
        };
        readonly LabelWidget m_hint = new() {
            Text = "输入消息...",
            Color = new Color(125, 125, 125),
            FontScale = 1f,
            VerticalAlignment = WidgetAlignment.Center,
            Margin = new Vector2(5f, 0f),
            IsHitTestVisible = false
        };
        readonly BevelledButtonWidget m_sendButton = new() {
            Size = new Vector2(SendButtonSize, SendButtonSize),
            CenterColor = Color.SkyBlue,
            BevelColor = Color.White,
            Text = "发送"
        };

        GameWidget m_gameWidget;
        BitmapButtonWidget m_messageButton;
        int m_renderedMessageCount;
        bool m_followHistoryBottom = true;
        bool m_focusInputNextFrame;

        public ChatWidget() {
            Size = new Vector2(PanelWidth, PanelHeight);
            // The root occupies the panel's full layout rectangle even while
            // the panel is hidden. Keep it out of hit testing so it cannot
            // block the controls behind it.
            IsHitTestVisible = false;
            m_panel.IsVisible = false;

            // Match the source NetMessageWidget: a 5 px inset message area,
            // with the input row anchored to the panel's bottom edge.
            m_panel.SetWidgetPosition(m_messageList, new Vector2(5f, 5f));
            m_panel.SetWidgetPosition(m_inputArea, new Vector2(0f, PanelHeight - InputHeight));
            m_panel.SetWidgetPosition(m_sendButton, new Vector2(PanelWidth - SendButtonSize, PanelHeight - InputHeight - 6f));

            m_messageList.AddChildren(m_history);
            m_inputArea.AddChildren(m_inputBackground);
            m_inputArea.AddChildren(m_input);
            m_inputArea.AddChildren(m_hint);
            m_panel.AddChildren(m_panelBackground);
            m_panel.AddChildren(m_messageList);
            m_panel.AddChildren(m_inputArea);
            m_panel.AddChildren(m_sendButton);
            AddChildren(m_panel);

            m_input.Enter += _ => {
                SendCurrentMessage();
                m_focusInputNextFrame = true;
            };
            m_input.Escape += _ => ClosePanel();
        }

        /// <summary>
        ///     Associates this widget with its owner view. The base game owns
        ///     the right-side button in GameWidget.xml; the mod only wires it
        ///     to this panel.
        /// </summary>
        public void SetGameWidget(GameWidget gameWidget) {
            if (ReferenceEquals(m_gameWidget, gameWidget)) {
                BindMessageButton();
                UpdateAvailability();
                return;
            }

            SetGameplayInputBlocked(false);
            ChatManager.MessageAdded -= OnMessageAdded;
            m_messageButton = null;
            m_gameWidget = gameWidget;
            SetGameplayInputBlocked(m_panel.IsVisible);
            if (m_gameWidget != null) {
                ChatManager.MessageAdded += OnMessageAdded;
                RebuildHistory();
                BindMessageButton();
            }
            UpdateAvailability();
        }

        void BindMessageButton() {
            if (m_gameWidget == null) {
                return;
            }

            m_messageButton = m_gameWidget.Children.Find<BitmapButtonWidget>("MsgButton", false);
            if (m_messageButton != null) {
                // The bitmap itself is the button label, as in the original.
                m_messageButton.Text = string.Empty;
            }
        }

        public override void Update() {
            base.Update();
            BindMessageButton();
            UpdateAvailability();
            if (!IsVisible || m_gameWidget?.PlayerData?.ComponentPlayer == null) {
                return;
            }

            if (m_messageButton?.IsClicked == true || m_sendButton.IsClicked) {
                if (m_messageButton?.IsClicked == true) {
                    TogglePanel();
                }
                else {
                    SendCurrentMessage();
                    m_focusInputNextFrame = true;
                }
            }

            if (m_focusInputNextFrame) {
                m_input.HasFocus = true;
                m_focusInputNextFrame = false;
            }

            // Preserve the original keyboard shortcuts.
            if (Input.IsKeyDownOnce(Key.Tab)) {
                TogglePanel();
            }
            else if (Input.IsKeyDownOnce(Key.Enter) && !m_input.HasFocus) {
                OpenPanel(true);
            }

            if (m_renderedMessageCount != ChatManager.Messages.Count) {
                RebuildHistory();
            }

            float contentLength = m_messageList.CalculateScrollAreaLength();
            if (contentLength > 0f) {
                float maxScroll = MathUtils.Max(0f, contentLength - m_messageList.ActualSize.Y);
                if ((Input.Scroll.HasValue && m_messageList.HitTestPanel(Input.Scroll.Value.XY))
                    || (Input.Drag.HasValue && m_messageList.HitTestPanel(Input.Drag.Value))) {
                    m_followHistoryBottom = false;
                }
                if (maxScroll - m_messageList.ScrollPosition <= 1f) {
                    m_followHistoryBottom = true;
                }
                if (m_followHistoryBottom) {
                    m_messageList.ScrollPosition = maxScroll;
                }
            }

            m_hint.IsVisible = string.IsNullOrEmpty(m_input.Text);
            SetGameplayInputBlocked(m_panel.IsVisible);
            m_gameWidget.PlayerData.ComponentPlayer.ComponentInput.AllowHandleInput = !m_input.HasFocus;
            Input.IsMouseCursorVisible = m_panel.IsVisible;
        }

        void SetGameplayInputBlocked(bool blocked) {
            ComponentPlayer componentPlayer = m_gameWidget?.PlayerData?.ComponentPlayer;
            if (componentPlayer?.ComponentInput != null) {
                componentPlayer.ComponentInput.GameplayInputBlocked = blocked;
            }
        }

        void UpdateAvailability() {
            bool available = m_gameWidget?.PlayerData?.IsMainPlayer == true
                && (NetworkManager.IsClientRunning || NetworkManager.IsServerRunning);
            if (!available) {
                ClosePanel();
            }

            IsVisible = available;
            if (m_messageButton != null) {
                m_messageButton.IsVisible = available;
            }
        }

        void TogglePanel() {
            if (m_panel.IsVisible) {
                ClosePanel();
            }
            else {
                OpenPanel(true);
            }
        }

        void OpenPanel(bool focusInput) {
            m_panel.IsVisible = true;
            SetGameplayInputBlocked(true);
            if (focusInput) {
                m_input.HasFocus = true;
            }
            Input.IsMouseCursorVisible = true;
        }

        void ClosePanel() {
            m_panel.IsVisible = false;
            SetGameplayInputBlocked(false);
            Input.IsMouseCursorVisible = false;
            if (m_input.HasFocus) {
                m_input.HasFocus = false;
            }
            if (m_gameWidget?.PlayerData?.ComponentPlayer != null) {
                m_gameWidget.PlayerData.ComponentPlayer.ComponentInput.AllowHandleInput = true;
            }
        }

        void SendCurrentMessage() {
            string text = (m_input.Text ?? string.Empty).Trim();
            if (text.Length == 0 || m_gameWidget?.PlayerData == null) {
                return;
            }

            if (text.Length > ChatMessagePacket.MaxTextLength) {
                text = text[..ChatMessagePacket.MaxTextLength];
            }

            PlayerData playerData = m_gameWidget.PlayerData;
            string name = string.IsNullOrEmpty(playerData.Name)
                ? $"Player {playerData.PlayerIndex}"
                : playerData.Name;
            int playerIndex = NetworkManager.PlayerIndex >= 0
                ? NetworkManager.PlayerIndex
                : playerData.PlayerIndex;
            NetworkManager.Queue(new ChatMessagePacket(ChatMessageType.Player, playerIndex, name, text));

            // A listen server without a client connection has no client-side
            // echo path, so add the host's message locally.
            if (!NetworkManager.IsClientRunning) {
                ChatManager.Add(ChatMessageType.Player, playerIndex, name, text);
            }

            m_input.Text = string.Empty;
        }

        void OnMessageAdded(ChatManager.ChatMessage message) {
            if (m_gameWidget?.PlayerData?.IsMainPlayer != true) {
                return;
            }

            RebuildHistory();
            m_followHistoryBottom = true;
            m_gameWidget.PlayerData.ComponentPlayer?.ComponentGui.DisplaySmallMessage(
                ChatManager.Format(message), Color.White, true, true);
        }

        void RebuildHistory() {
            m_history.ClearChildren();
            foreach (ChatManager.ChatMessage message in ChatManager.Messages) {
                LabelWidget label = new() {
                    Text = ChatManager.Format(message),
                    FontScale = 1f,
                    Color = Color.White,
                    DropShadow = true,
                    WordWrap = true,
                    Margin = new Vector2(5f, 1f),
                    IsHitTestVisible = false
                };
                m_history.AddChildren(label);
            }
            m_renderedMessageCount = ChatManager.Messages.Count;
        }

        public override void Dispose() {
            SetGameplayInputBlocked(false);
            ChatManager.MessageAdded -= OnMessageAdded;
            if (m_input.HasFocus) {
                m_input.HasFocus = false;
            }
            base.Dispose();
        }
    }
}
