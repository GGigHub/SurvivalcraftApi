using Engine;

namespace Game.Network;

public class MovingBlockRemovePacket : Packet {
    public override byte ID => (byte)PacketType.MovingBlockRemove;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public string movingBlockId;
    public Point3 m_position;
    public bool m_hasTag;
    public bool m_stop;
    public long m_instanceId;
    public long m_generation;
    public long m_revision;

    public MovingBlockRemovePacket() { }

    public MovingBlockRemovePacket(IMovingBlockSet movingSet, bool stop) {
        movingBlockId = movingSet.Id;
        m_stop = stop;
        if (movingSet.Tag is Point3 tagPosition) {
            m_hasTag = true;
            m_position = tagPosition;
        }
        m_instanceId = NetworkMovingBlockIdentityTracker.GetOrCreate(
            (SubsystemMovingBlocks.MovingBlockSet)movingSet);
        if (GameManager.Project != null) {
            NetworkLogicStateKey key = StateKey();
            m_generation = NetworkLogicStateTracker.EnsureAuthoritativeMovingGeneration(
                GameManager.Project,
                key);
            m_revision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                key);
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref movingBlockId);
        archive.Value(ref m_hasTag);
        if (m_hasTag) {
            archive.Value(ref m_position);
        }
        archive.Value(ref m_stop);
        archive.Value(ref m_instanceId);
        archive.Value(ref m_generation);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || string.IsNullOrWhiteSpace(movingBlockId)
            || movingBlockId.Length > 256
            || m_instanceId <= 0
            || m_generation <= 0
            || m_revision <= 0
            || GameManager.Project == null) {
            return;
        }
        SubsystemMovingBlocks subsystemMovingBlocks = GameManager.Project.FindSubsystem<SubsystemMovingBlocks>();
        IMovingBlockSet movingSet = null;
        bool hasRegisteredInstance = NetworkMovingBlockIdentityTracker.TryGet(
                GameManager.Project,
                m_instanceId,
                out SubsystemMovingBlocks.MovingBlockSet existingInstance);
        if (hasRegisteredInstance
            && subsystemMovingBlocks.m_movingBlockSets.Contains(existingInstance)) {
            movingSet = existingInstance;
        }
        else {
            if (hasRegisteredInstance) {
                NetworkMovingBlockIdentityTracker.Unregister(
                    GameManager.Project,
                    existingInstance);
            }
            if (m_hasTag) {
                movingSet = subsystemMovingBlocks.FindMovingBlocks(movingBlockId, m_position);
            }
        }
        if (!NetworkLogicStateTracker.ReceiveMoving(
                GameManager.Project,
                StateKey(),
                m_generation,
                m_revision,
                present: false,
                forceRefresh: movingSet != null).Apply) {
            return;
        }
        if (movingSet == null) {
            return;
        }
        if (m_stop) {
            subsystemMovingBlocks.DoStop(movingSet as SubsystemMovingBlocks.MovingBlockSet);
        }
        else {
            subsystemMovingBlocks.RemoveMovingBlockSet(movingSet, true);
        }
        NetworkMovingBlockIdentityTracker.Unregister(
            GameManager.Project,
            (SubsystemMovingBlocks.MovingBlockSet)movingSet);
    }

    private NetworkLogicStateKey StateKey() =>
        NetworkMovingBlockIdentityTracker.CreateStateKey(
            movingBlockId,
            m_hasTag,
            m_position,
            m_instanceId);
}

