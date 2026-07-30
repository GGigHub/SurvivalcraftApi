using Engine;

namespace Game.Network;

public class PickableSyncListPacket : Packet {
    public override byte ID => (byte)PacketType.PickableSyncList;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<Pickable> m_pickables = [];
    public List<PickableLifecycleStamp> m_lifecycleStamps = [];

    public PickableSyncListPacket() { }

    public PickableSyncListPacket(IEnumerable<Pickable> pickables) {
        m_pickables = [.. pickables];
        m_lifecycleStamps = PickablePacketHelpers.CreateLifecycleStamps(m_pickables);
    }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePacketHelpers.ReadList(archive, PickablePacketHelpers.SerializeFullPickable);
        }
        else {
            if (m_lifecycleStamps.Count == 0 && m_pickables.Count > 0) {
                m_lifecycleStamps = PickablePacketHelpers.CreateLifecycleStamps(m_pickables);
            }
            PickablePacketHelpers.WriteList(
                archive,
                m_pickables,
                PickablePacketHelpers.SerializeFullPickable);
        }
        PickablePacketHelpers.SerializeLifecycleStamps(archive, ref m_lifecycleStamps);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        IReadOnlyDictionary<int, long> revisions =
            PickablePacketHelpers.BuildLifecycleMap(m_lifecycleStamps);
        foreach (Pickable pickable in m_pickables) {
            if (revisions.TryGetValue(pickable.Id, out long revision)) {
                PickablePacketHelpers.TryApplyCreate(subsystemPickables, pickable, revision);
            }
        }
    }
}

