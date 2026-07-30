using Engine;

namespace Game.Network;

public class PickableDeleteListPacket : Packet {
    public override byte ID => (byte)PacketType.PickableDeleteList;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<Pickable> m_pickables = [];
    public List<PickableLifecycleStamp> m_lifecycleStamps = [];

    public PickableDeleteListPacket() { }

    public PickableDeleteListPacket(
        IEnumerable<Pickable> pickables,
        IEnumerable<PickableLifecycleStamp> lifecycleStamps = null) {
        m_pickables = [.. pickables];
        m_lifecycleStamps = lifecycleStamps?.ToList()
            ?? PickablePacketHelpers.CreateLifecycleStamps(m_pickables);
    }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePacketHelpers.ReadList(archive, PickablePacketHelpers.SerializeDeletePickable);
        }
        else {
            PickablePacketHelpers.WriteList(archive, m_pickables, PickablePacketHelpers.SerializeDeletePickable);
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
                PickablePacketHelpers.TryApplyRemoval(
                    subsystemPickables,
                    pickable.Id,
                    revision,
                    playSound: false);
            }
        }
    }
}

