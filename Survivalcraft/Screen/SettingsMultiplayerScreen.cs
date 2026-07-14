using System.Xml.Linq;
using Engine;

namespace Game {
    public class SettingsMultiplayerScreen : Screen {
        public ButtonWidget m_playWithFriendsButton;
        public ButtonWidget m_showPlayWithFriendsDialogButton;

        public SettingsMultiplayerScreen() {
            XElement node = ContentManager.Get<XElement>("Screens/SettingsMultiplayerScreen");
            LoadContents(this, node);
            m_playWithFriendsButton = Children.Find<ButtonWidget>("PlayWithFriendsButton");
            m_showPlayWithFriendsDialogButton = Children.Find<ButtonWidget>("ShowPlayWithFriendsDialogButton");
        }

        public override void Update() {
            if (m_playWithFriendsButton.IsClicked) {
                SettingsManager.PlayWithFriendsEnabled = !SettingsManager.PlayWithFriendsEnabled;
                SettingsManager.SaveSettings();
            }
            if (m_showPlayWithFriendsDialogButton.IsClicked) {
                SettingsManager.ShowPlayWithFriendsDialog = !SettingsManager.ShowPlayWithFriendsDialog;
                SettingsManager.SaveSettings();
            }

            m_playWithFriendsButton.Text = SettingsManager.PlayWithFriendsEnabled ? LanguageControl.Yes : LanguageControl.No;
            m_showPlayWithFriendsDialogButton.Text = SettingsManager.ShowPlayWithFriendsDialog ? LanguageControl.Yes : LanguageControl.No;

            if (Input.Back || Input.Cancel || Children.Find<ButtonWidget>("TopBar.Back").IsClicked) {
                ScreensManager.GoBack();
            }
        }
    }
}
