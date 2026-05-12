using Engine;

namespace Game.Network;

public class MovingBlockRemovePacket : Packet {
    public override byte ID => (byte)PacketType.MovingBlockRemove;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;

    public string movingBlockId;
    public Point3 m_position;
    public bool m_hasTag;
    public bool m_stop;

    public MovingBlockRemovePacket() { }

    public MovingBlockRemovePacket(IMovingBlockSet movingSet, bool stop) {
        movingBlockId = movingSet.Id;
        m_stop = stop;
        if (movingSet.Tag != null) {
            m_hasTag = true;
            m_position = (movingSet.Tag as Point3?).Value;
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref movingBlockId);
        archive.Value(ref m_hasTag);
        if (m_hasTag) {
            archive.Value(ref m_position);
        }
        archive.Value(ref m_stop);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemMovingBlocks subsystemMovingBlocks = GameManager.Project.FindSubsystem<SubsystemMovingBlocks>();
        IMovingBlockSet movingSet = m_hasTag
            ? subsystemMovingBlocks.FindMovingBlocks(movingBlockId, m_position)
            : subsystemMovingBlocks.FindMovingBlocks(movingBlockId, null);
        if (movingSet == null) {
            return;
        }
        if (m_stop) {
            subsystemMovingBlocks.DoStop(movingSet as SubsystemMovingBlocks.MovingBlockSet);
        }
        else {
            subsystemMovingBlocks.RemoveMovingBlockSetLogic(movingSet);
        }
    }
}

