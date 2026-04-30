using Engine;

namespace Game.NetWork.Packages;

internal static class MovingBlockPackageHelpers {
    public static void SerializeBlockList(PackageSerializer archive, List<MovingBlockAddPackage.MovingBlockData> blocks) {
        int count = archive.IsWriting ? blocks.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            blocks.Clear();
            if (count < 0) {
                throw new InvalidDataException($"MovingBlockPackage contains invalid block count: {count}");
            }
            for (int i = 0; i < count; i++) {
                MovingBlockAddPackage.MovingBlockData data = new();
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
                blocks.Add(data);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                MovingBlockAddPackage.MovingBlockData data = blocks[i];
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
            }
        }
    }
}

public class MovingBlockAddPackage : IPackage {
    public override byte ID => (byte)PackageType.MovingBlockAdd;
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

    public MovingBlockAddPackage() { }

    public MovingBlockAddPackage(SubsystemMovingBlocks.MovingBlockSet movingSet) {
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

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_addPosition);
        archive.Value(ref m_addTargetPosition);
        archive.Value(ref m_addSpeed);
        archive.Value(ref m_addAcceleration);
        archive.Value(ref m_addDrag);
        archive.Value(ref m_addSmoothness);
        archive.Value(ref m_addId);
        MovingBlockPackageHelpers.SerializeBlockList(archive, m_addBlocks);
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
}

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
