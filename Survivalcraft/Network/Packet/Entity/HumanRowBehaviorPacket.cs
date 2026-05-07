using Engine;
using GameEntitySystem;

namespace Game.Network;

public class HumanRowBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorHumanRow;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_rowLeft;
    public bool m_rowRight;
    public HumanRowBehaviorPacket() { }
    public HumanRowBehaviorPacket(ComponentHumanModel model) {
        m_entityId = model.Entity.Id;
        m_rowLeft = model.m_rowLeft;
        m_rowRight = model.m_rowRight;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_rowLeft);
        archive.Value(ref m_rowRight);
    }
    public override void Handle(bool isServer) {
        BehaviorHelpers.WithEntity(m_entityId, entity => {
            ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
            if (humanModel != null) {
                humanModel.hasData = true;
                humanModel.m_rowLeft = m_rowLeft;
                humanModel.m_rowRight = m_rowRight;
                Random random = new();
                GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", random.Float(0.4f, 0.6f), random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
                BehaviorHelpers.BroadcastFromServer(this, isServer);
            }
        });
    }
}

