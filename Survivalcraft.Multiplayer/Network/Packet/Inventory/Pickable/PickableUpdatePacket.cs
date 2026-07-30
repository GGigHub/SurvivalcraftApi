using Engine;

namespace Game.Network;

public class PickableUpdatePacket : Packet {
    public override byte ID => (byte)PacketType.PickableUpdate;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<Pickable> m_pickables = [];
    public List<PickableLifecycleStamp> m_lifecycleStamps = [];

    public PickableUpdatePacket() { }

    public PickableUpdatePacket(
        List<Pickable> pickables,
        IEnumerable<PickableLifecycleStamp> lifecycleStamps = null) {
        m_pickables.AddRange(pickables);
        m_lifecycleStamps = lifecycleStamps?.ToList()
            ?? PickablePacketHelpers.CreateLifecycleStamps(pickables);
    }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePacketHelpers.ReadList(archive, PickablePacketHelpers.SerializeUpdatePickable);
        }
        else {
            PickablePacketHelpers.WriteList(archive, m_pickables, PickablePacketHelpers.SerializeUpdatePickable);
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
                PickablePacketHelpers.TryApplyUpdate(subsystemPickables, pickable, revision);
            }
        }
    }
}
