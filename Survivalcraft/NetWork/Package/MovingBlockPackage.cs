using Engine;
using System.Collections.Generic;
using static Game.NetWork.Packages.PickablePackage;

namespace Game.NetWork.Packages;

public class MovingBlockPackage : IPackage
{
    public byte ID => (byte)PackageType.MovingBlockSet;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public enum EventType
    {
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

    public struct MovingBlockData
    {
        public Point3 Offset;
        public int Value;
    }

    public MovingBlockPackage() { }

    public MovingBlockPackage(SubsystemMovingBlocks.MovingBlockSet movingSet)
    {
        m_type = EventType.Add;
        m_addPosition = movingSet.Position;
        m_addTargetPosition = movingSet.TargetPosition;
        m_addSpeed = movingSet.Speed;
        m_addAcceleration = movingSet.Acceleration;
        m_addDrag = movingSet.Drag;
        m_addSmoothness = movingSet.Smoothness;
        m_addId = movingSet.Id;
        foreach (MovingBlock block in movingSet.Blocks)
        {
            m_addBlocks.Add(new MovingBlockData
            {
                Offset = block.Offset,
                Value = block.Value
            });
        }
    }

    public MovingBlockPackage(IMovingBlockSet movingSet, bool stop)
    {
        m_type = EventType.Remove;
        movingBlockId = movingSet.Id;
        if (stop) m_type |= EventType.Stopped;
        if (movingSet.Tag != null)
        {
            m_type |= EventType.HagTag;
            m_position = (movingSet.Tag as Point3?).Value;
        }
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case EventType.Add:
                writer.Write(m_addPosition);
                writer.Write(m_addTargetPosition);
                writer.Write(m_addSpeed);
                writer.Write(m_addAcceleration);
                writer.Write(m_addDrag);
                writer.Write(m_addSmoothness);
                writer.Write(m_addId);
                writer.Write(m_addBlocks.Count);
                foreach (MovingBlockData block in m_addBlocks)
                {
                    writer.Write(block.Offset);
                    writer.Write(block.Value);
                }
                break;
            default:
                writer.Write(movingBlockId);
                if (m_type.HasFlag(EventType.HagTag))
                {
                    writer.Write(m_position);
                }
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<EventType>();
        switch (m_type)
        {
            case EventType.Add:
                m_addPosition = reader.ReadVector3();
                m_addTargetPosition = reader.ReadVector3();
                m_addSpeed = reader.ReadSingle();
                m_addAcceleration = reader.ReadSingle();
                m_addDrag = reader.ReadSingle();
                m_addSmoothness = reader.ReadVector2();
                m_addId = reader.ReadString();
                m_addBlocks.Clear();
                int count = reader.ReadInt32();
                if (count < 0)
                {
                    throw new InvalidDataException($"MovingBlockPackage contains invalid block count: {count}");
                }
                for (int i = 0; i < count; i++)
                {
                    m_addBlocks.Add(new MovingBlockData
                    {
                        Offset = reader.ReadPoint3(),
                        Value = reader.ReadInt32()
                    });
                }
                break;
            default:
                movingBlockId = reader.ReadString();
                if (m_type.HasFlag(EventType.HagTag))
                {
                    m_position = reader.ReadPoint3();
                }
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (isServer)
        {
            return;
        }
        var obj = projectNet.FindSubsystem<SubsystemMovingBlocks>();
        switch (m_type)
        {
            case EventType.Add:
                List<MovingBlock> blocks = new(m_addBlocks.Count);
                foreach (MovingBlockData block in m_addBlocks)
                {
                    blocks.Add(new MovingBlock
                    {
                        Offset = block.Offset,
                        Value = block.Value
                    });
                }
                var m = obj.AddMovingBlockSet(
                    m_addPosition,
                    m_addTargetPosition,
                    m_addSpeed,
                    m_addAcceleration,
                    m_addDrag,
                    m_addSmoothness,
                    blocks,
                    m_addId,
                    null,
                    false
                ) as SubsystemMovingBlocks.MovingBlockSet;
                var m_subsystemAudio = projectNet.FindSubsystem<SubsystemAudio>();
                if (m != null && m.Id == SubsystemPistonBlockBehavior.IdString)
                {
                    m_subsystemAudio.PlaySound("Audio/Piston", 1f, 0f, m.Position, 2f, autoDelay: true);
                }
                break;
            default:
                IMovingBlockSet mm;
                if (m_type.HasFlag(EventType.HagTag)) mm = obj.FindMovingBlocks(movingBlockId, m_position);
                else mm = obj.FindMovingBlocks(movingBlockId, null);
                if (mm != null)
                {
                    if (m_type.HasFlag(EventType.Stopped))
                    {
                        obj.DoStop(mm as SubsystemMovingBlocks.MovingBlockSet);
                    }
                    else
                    {
                        obj.RemoveMovingBlockSetLogic(mm);
                    }
                }
                break;
        }
    }
}

