using System.Xml.Linq;

namespace Game {
    public sealed class PlayWithFriendsDialog : Dialog {
        private readonly Action<bool> m_handler;
        private readonly CheckboxWidget m_doNotAskAgainWidget;
        private readonly ButtonWidget m_yesButtonWidget;
        private readonly ButtonWidget m_noButtonWidget;

        public bool AutoHide { get; set; } = true;

        public PlayWithFriendsDialog(Action<bool> handler) {
            m_handler = handler;
            XElement node = ContentManager.Get<XElement>("Dialogs/PlayWithFriendsDialog");
            LoadContents(this, node);
            m_doNotAskAgainWidget = Children.Find<CheckboxWidget>("PlayWithFriendsDialog.DoNotAskAgain");
            m_yesButtonWidget = Children.Find<ButtonWidget>("PlayWithFriendsDialog.Yes");
            m_noButtonWidget = Children.Find<ButtonWidget>("PlayWithFriendsDialog.No");
        }

        public override void Update() {
            if (Input.Cancel || m_noButtonWidget.IsClicked) {
                Dismiss(false);
            }
            else if (Input.Ok || m_yesButtonWidget.IsClicked) {
                Dismiss(true);
            }
        }

        private void Dismiss(bool playWithFriends) {
            if (m_doNotAskAgainWidget.IsChecked) {
                SettingsManager.ShowPlayWithFriendsDialog = false;
            }
            SettingsManager.PlayWithFriendsEnabled = playWithFriends;
            SettingsManager.SaveSettings();
            if (AutoHide) {
                DialogsManager.HideDialog(this);
            }
            m_handler?.Invoke(playWithFriends);
        }

        public static void ShowIfNeeded(Action<bool> handler) {
            if (SettingsManager.ShowPlayWithFriendsDialog) {
                DialogsManager.ShowDialog(null, new PlayWithFriendsDialog(handler));
            }
            else {
                handler?.Invoke(SettingsManager.PlayWithFriendsEnabled);
            }
        }
    }
}
