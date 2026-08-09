using Engine;
using GameEntitySystem;

namespace Game.Network;

public class HumanRowBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorHumanRow;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_rowLeft;
    public bool m_rowRight;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public HumanRowBehaviorPacket() { }
    public HumanRowBehaviorPacket(ComponentHumanModel model, long revision = -1) {
        m_entityId = model.Entity.Id;
        m_rowLeft = model.m_rowLeft;
        m_rowRight = model.m_rowRight;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(model);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            model,
            NetworkStateRevisionDomain.HumanRow,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_rowLeft);
        archive.Value(ref m_rowRight);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (!NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.HumanRow,
                out ComponentHumanModel humanModel)) {
            return;
        }
        ApplyRow(humanModel);
    }

    public void ApplyRow(ComponentHumanModel humanModel) {
        humanModel.hasData = true;
        humanModel.m_rowHoldEndTime = GameManager.Project.FindSubsystem<SubsystemGameInfo>().TotalElapsedGameTime + 0.75;
        humanModel.m_rowLeft = m_rowLeft;
        humanModel.m_rowRight = m_rowRight;
        humanModel.RowLeftOrder = m_rowLeft;
        humanModel.RowRightOrder = m_rowRight;
        Random random = new();
        GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", random.Float(0.4f, 0.6f), random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
    }
}
