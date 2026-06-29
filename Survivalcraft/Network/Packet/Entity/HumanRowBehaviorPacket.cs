using Engine;
using GameEntitySystem;

namespace Game.Network;

public class HumanRowBehaviorPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.BehaviorHumanRow;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_rowLeft;
    public bool m_rowRight;
    public HumanRowBehaviorPacket() { }
    public HumanRowBehaviorPacket(ComponentHumanModel model) {
        ComponentPlayer player = model.Entity.FindComponent<ComponentPlayer>();
        m_fromPlayerIndex = player?.PlayerData?.PlayerIndex ?? -1;
        m_entityId = model.Entity.Id;
        m_rowLeft = model.m_rowLeft;
        m_rowRight = model.m_rowRight;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_fromPlayerIndex);
        archive.Value(ref m_entityId);
        archive.Value(ref m_rowLeft);
        archive.Value(ref m_rowRight);
    }
    public override void Handle(bool isServer) {
        bool applied = false;
        ComponentHumanModel playerHumanModel = null;
        if (m_fromPlayerIndex >= 0) {
            if (TryResolvePlayer(isServer, out PlayerData playerData)) {
                playerHumanModel = playerData.ComponentPlayer?.ComponentCreatureModel as ComponentHumanModel
                    ?? playerData.ComponentPlayer?.Entity.FindComponent<ComponentHumanModel>();
                if (playerHumanModel != null) {
                    ApplyRow(playerHumanModel);
                    applied = true;
                }
            }
            else if (isServer) {
                return;
            }
        }
        if (m_entityId != 0) {
            foreach (Entity entity in GameManager.Project.Entities) {
                if (entity.Id != m_entityId) {
                    continue;
                }
                ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
                if (humanModel == null || humanModel == playerHumanModel) {
                    continue;
                }
                ApplyRow(humanModel);
                applied = true;
            }
        }
        if (applied) {
            QueuePlayerPacket(this, isServer);
        }
    }

    public void ApplyRow(ComponentHumanModel humanModel) {
        humanModel.hasData = true;
        humanModel.m_rowLeft = m_rowLeft;
        humanModel.m_rowRight = m_rowRight;
        Random random = new();
        GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", random.Float(0.4f, 0.6f), random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
    }
}
