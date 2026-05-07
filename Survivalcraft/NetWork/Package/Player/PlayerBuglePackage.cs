using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerBuglePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerBugle;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;
    public string m_title;
    public string m_content;

    public PlayerBuglePackage() { }

    public PlayerBuglePackage(Guid guid, string title, string content) {
        m_playerGuid = guid;
        m_title = title;
        m_content = content;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
        archive.Value(ref m_title);
        archive.Value(ref m_content);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (mainPlayer != null) {
            if (m_playerGuid == Guid.Empty || (m_playerGuid != Guid.Empty && mainPlayer.PlayerData.PlayerGUID == m_playerGuid)) {
                DialogsManager.HideAllDialogs();
                mainPlayer.ComponentHealth.IsInvulnerable = true;
                string content = m_content.Replace("[n]", "\n").Replace("[e]", " ");
                DialogsManager.ShowDialog(null, new MessageDialog(m_title, content, LanguageControl.Ok, null, btn => {
                    DialogsManager.HideAllDialogs();
                    mainPlayer.ComponentHealth.IsInvulnerable = false;
                }));
            }
        }
    }
}