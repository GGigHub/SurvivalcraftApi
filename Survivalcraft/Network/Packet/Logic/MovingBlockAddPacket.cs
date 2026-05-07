using Engine;

namespace Game.Network;

public class MovingBlockAddPacket : Packet {
    public override byte ID => (byte)PacketType.MovingBlockAdd;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public Vector3 m_addPosition;
    public Vector3 m_addTargetPosition;
    public float m_addSpeed;
    public float m_addAcceleration;
    public float m_addDrag;
    public Vector2 m_addSmoothness;
    public string m_addId;
    public readonly List<MovingBlockData> m_addBlocks = [];

    public struct MovingBlockData {
        public Point3 Offset;
        public int Value;
    }

    public MovingBlockAddPacket() { }

    public MovingBlockAddPacket(SubsystemMovingBlocks.MovingBlockSet movingSet) {
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

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_addPosition);
        archive.Value(ref m_addTargetPosition);
        archive.Value(ref m_addSpeed);
        archive.Value(ref m_addAcceleration);
        archive.Value(ref m_addDrag);
        archive.Value(ref m_addSmoothness);
        archive.Value(ref m_addId);
        SerializeBlockList(archive, m_addBlocks);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemMovingBlocks subsystemMovingBlocks = GameManager.Project.FindSubsystem<SubsystemMovingBlocks>();
        List<MovingBlock> blocks = new(m_addBlocks.Count);
        foreach (MovingBlockData block in m_addBlocks) {
            blocks.Add(new MovingBlock {
                Offset = block.Offset,
                Value = block.Value
            });
        }
        var movingSet = subsystemMovingBlocks.AddMovingBlockSet(m_addPosition, m_addTargetPosition, m_addSpeed, m_addAcceleration, m_addDrag, m_addSmoothness, blocks, m_addId, null, false) as SubsystemMovingBlocks.MovingBlockSet;
        if (movingSet != null && movingSet.Id == SubsystemPistonBlockBehavior.IdString) {
            GameManager.Project.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Piston", 1f, 0f, movingSet.Position, 2f, autoDelay: true);
        }
    }

    public static void SerializeBlockList(PacketSerializer archive, List<MovingBlockAddPacket.MovingBlockData> blocks) {
        int count = archive.IsWriting ? blocks.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            blocks.Clear();
            if (count < 0) {
                throw new InvalidDataException($"MovingBlockPacket contains invalid block count: {count}");
            }
            for (int i = 0; i < count; i++) {
                MovingBlockAddPacket.MovingBlockData data = new();
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
                blocks.Add(data);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                MovingBlockAddPacket.MovingBlockData data = blocks[i];
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
            }
        }
    }
}

