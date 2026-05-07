using Engine;

namespace Game.NetWork;

public class MovingBlockRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.MovingBlockRemove;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public string movingBlockId;
    public Point3 m_position;
    public bool m_hasTag;
    public bool m_stop;

    public MovingBlockRemovePackage() { }

    public MovingBlockRemovePackage(IMovingBlockSet movingSet, bool stop) {
        movingBlockId = movingSet.Id;
        m_stop = stop;
        if (movingSet.Tag != null) {
            m_hasTag = true;
            m_position = (movingSet.Tag as Point3?).Value;
        }
    }

    public override void Serialize(PackageSerializer archive) {
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