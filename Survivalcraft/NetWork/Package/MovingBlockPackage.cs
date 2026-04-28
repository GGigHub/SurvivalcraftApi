using Engine;

namespace Game.NetWork.Packages;

public class MovingBlockPackage : IPackage {
    public override byte ID => (byte)PackageType.MovingBlockSet;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public enum EventType {
        Add = 1,
        Remove = 2,
        HagTag = 4,
        Stopped = 8
    }

    public EventType m_type;
    public Vector3 m_addPosition;
    public Vector3 m_addTargetPosition;
    public float m_addSpeed;
    public float m_addAcceleration;
    public float m_addDrag;
    public Vector2 m_addSmoothness;
    public string m_addId;
    public readonly List<MovingBlockData> m_addBlocks = [];
    public string movingBlockId;
    public Point3 m_position;

    public struct MovingBlockData {
        public Point3 Offset;
        public int Value;
    }

    public MovingBlockPackage() {
    }

    public MovingBlockPackage(SubsystemMovingBlocks.MovingBlockSet movingSet) {
        m_type = EventType.Add;
        m_addPosition = movingSet.Position;
        m_addTargetPosition = movingSet.TargetPosition;
        m_addSpeed = movingSet.Speed;
        m_addAcceleration = movingSet.Acceleration;
        m_addDrag = movingSet.Drag;
        m_addSmoothness = movingSet.Smoothness;
        m_addId = movingSet.Id;
        foreach (MovingBlock block in movingSet.Blocks) {
            m_addBlocks.Add(new MovingBlockData {
                Offset = block.Offset,
                Value = block.Value
            });
        }
    }

    public MovingBlockPackage(IMovingBlockSet movingSet, bool stop) {
        m_type = EventType.Remove;
        movingBlockId = movingSet.Id;
        if (stop) m_type |= EventType.Stopped;
        if (movingSet.Tag != null) {
            m_type |= EventType.HagTag;
            m_position = (movingSet.Tag as Point3?).Value;
        }
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case EventType.Add:
                archive.Value(ref m_addPosition);
                archive.Value(ref m_addTargetPosition);
                archive.Value(ref m_addSpeed);
                archive.Value(ref m_addAcceleration);
                archive.Value(ref m_addDrag);
                archive.Value(ref m_addSmoothness);
                archive.Value(ref m_addId);
                int count = archive.IsWriting ? m_addBlocks.Count : 0;
                archive.Value(ref count);
                if (archive.IsReading) {
                    m_addBlocks.Clear();
                    if (count < 0) {
                        throw new InvalidDataException($"MovingBlockPackage contains invalid block count: {count}");
                    }
                    for (int i = 0; i < count; i++) {
                        MovingBlockData data = new MovingBlockData();
                        archive.Value(ref data.Offset);
                        archive.Value(ref data.Value);
                        m_addBlocks.Add(data);
                    }
                }
                else {
                    for (int i = 0; i < count; i++) {
                        MovingBlockData data = m_addBlocks[i];
                        archive.Value(ref data.Offset);
                        archive.Value(ref data.Value);
                    }
                }
                break;
            default:
                archive.Value(ref movingBlockId);
                if (m_type.HasFlag(EventType.HagTag)) {
                    archive.Value(ref m_position);
                }
                break;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        var obj = GameManager.Project.FindSubsystem<SubsystemMovingBlocks>();
        switch (m_type) {
            case EventType.Add:
                List<MovingBlock> blocks = new(m_addBlocks.Count);
                foreach (MovingBlockData block in m_addBlocks) {
                    blocks.Add(new MovingBlock {
                        Offset = block.Offset,
                        Value = block.Value
                    });
                }
                var m = obj.AddMovingBlockSet(m_addPosition, m_addTargetPosition, m_addSpeed, m_addAcceleration, m_addDrag, m_addSmoothness, blocks, m_addId, null, false) as SubsystemMovingBlocks.MovingBlockSet;
                var m_subsystemAudio = GameManager.Project.FindSubsystem<SubsystemAudio>();
                if (m != null && m.Id == SubsystemPistonBlockBehavior.IdString) {
                    m_subsystemAudio.PlaySound("Audio/Piston", 1f, 0f, m.Position, 2f, autoDelay: true);
                }
                break;
            default:
                IMovingBlockSet mm;
                if (m_type.HasFlag(EventType.HagTag)) mm = obj.FindMovingBlocks(movingBlockId, m_position);
                else mm = obj.FindMovingBlocks(movingBlockId, null);
                if (mm != null) {
                    if (m_type.HasFlag(EventType.Stopped)) {
                        obj.DoStop(mm as SubsystemMovingBlocks.MovingBlockSet);
                    }
                    else {
                        obj.RemoveMovingBlockSetLogic(mm);
                    }
                }
                break;
        }
    }
}
