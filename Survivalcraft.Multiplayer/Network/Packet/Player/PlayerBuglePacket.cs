using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerBuglePacket : Packet {
    public override byte ID => (byte)PacketType.PlayerBugle;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public override bool RequiresProject => true;
    public int m_playerIndex;
    public string m_title;
    public string m_content;

    public PlayerBuglePacket() { }

    public PlayerBuglePacket(int playerIndex, string title, string content) {
        m_playerIndex = playerIndex;
        m_title = title;
        m_content = content;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerIndex);
        archive.Value(ref m_title);
        archive.Value(ref m_content);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (mainPlayer != null) {
            if (m_playerIndex < 0 || mainPlayer.PlayerData.PlayerIndex == m_playerIndex) {
                DialogsManager.HideAllDialogs();
                mainPlayer.ComponentHealth.IsInvulnerable = true;
                string content = m_content.Replace("[n]", "\n").Replace("[e]", " ");
                MessageDialog dialog = null;
                void RestoreInvulnerability() {
                    if (mainPlayer.ComponentHealth != null) {
                        mainPlayer.ComponentHealth.IsInvulnerable = false;
                    }
                    DialogsManager.DialogHidden -= OnDialogHidden;
                }
                void OnDialogHidden(Dialog hiddenDialog) {
                    if (hiddenDialog == dialog) {
                        RestoreInvulnerability();
                    }
                }
                dialog = new MessageDialog(m_title, content, LanguageControl.Ok, null, btn => {
                    DialogsManager.HideAllDialogs();
                    RestoreInvulnerability();
                });
                DialogsManager.DialogHidden += OnDialogHidden;
                DialogsManager.ShowDialog(null, dialog);
            }
        }
    }
}

