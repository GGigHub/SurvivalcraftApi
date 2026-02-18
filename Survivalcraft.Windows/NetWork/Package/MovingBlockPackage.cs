using Engine;
using TemplatesDatabase;
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

    public ValuesDictionary AddData;

    public string movingBlockId;

    public Point3 m_position;

    public MovingBlockPackage() { }

    public MovingBlockPackage(SubsystemMovingBlocks.MovingBlockSet movingSet)
    {
        m_type = EventType.Add;
        AddData = SubsystemMovingBlocks.SaveMovingItem(movingSet);
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
                writer.Write(AddData);
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
                AddData = reader.ReadValuesDictionary();
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

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        if (isServer)
        {
            return;
        }
        var obj = projectNet.FindSubsystem<SubsystemMovingBlocks>();
        switch (m_type)
        {
            case EventType.Add:
                var m = obj.LoadAndAddMovingItem(AddData) as SubsystemMovingBlocks.MovingBlockSet;
                var m_subsystemAudio = projectNet.FindSubsystem<SubsystemAudio>();
                obj.m_movingBlockSets.Add(m);
                if (m.Id == SubsystemPistonBlockBehavior.IdString)
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

