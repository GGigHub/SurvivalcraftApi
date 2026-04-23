using Engine;
using System.Collections.Generic;

namespace Game.NetWork.Packages;

public class SubsystemBodyPackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemBody;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.Playing;
    public enum EventType
    {
        BodyUpdate,
        ApplyImpulse,
        HandleAxisCollision
    }
    private struct BodyItem
    {
        public ushort CreatureId;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector2 LookAngles;
        public Vector3? FlyOrder;
        public ChangeFlag changeFlag;
    }

    public ushort CreatureId;
    public ushort TargetCreatureId;
    public Vector3 m_Impulse;
    public EventType m_eventType;

    public ComponentModel CreatureModel;

    public enum ChangeFlag : byte
    {
        None = 0,
        LookAnglesChange = 1,
        FlyOrderChange = 2,
        PositionChange = 4,
        RotationChange = 8,
        VelocityChange = 16
    }

    private List<BodyItem> m_bodyList = new List<BodyItem>();
    public SubsystemBodyPackage() { }
    public SubsystemBodyPackage(List<ComponentBody> bodies)
    {
        m_eventType = EventType.BodyUpdate;
        foreach (var b in bodies)
        {
            AddItem(b);
        }
    }

    public void AddItem(ComponentBody body)
    {
        BodyItem bodyItem = new BodyItem();
        bodyItem.CreatureId = body.Entity.EntityId;
        if (body.SendPosition.HasValue)
        {
            bodyItem.changeFlag |= ChangeFlag.PositionChange;
            bodyItem.Position = body.SendPosition.Value;
        }
        if (body.SendRotation.HasValue)
        {
            bodyItem.changeFlag |= ChangeFlag.RotationChange;
            bodyItem.Rotation = body.SendRotation.Value;
        }
        if (body.SendVelocity.HasValue)
        {
            bodyItem.changeFlag |= ChangeFlag.VelocityChange;
            bodyItem.Velocity = body.SendVelocity.Value;
        }
        if (body.Locomotion != null)
        {
            if (body.Locomotion.SendLookAngles.HasValue)
            {
                bodyItem.changeFlag |= ChangeFlag.LookAnglesChange;
                bodyItem.LookAngles = body.Locomotion.SendLookAngles.Value;
            }
            if (body.Locomotion.FlyOrderChange)
            {
                bodyItem.changeFlag |= ChangeFlag.FlyOrderChange;
                bodyItem.FlyOrder = body.Locomotion.LastFlyOrder;
            }
        }
        m_bodyList.Add(bodyItem);
    }

    public SubsystemBodyPackage(ComponentBody body, Vector3 vector)
    {
        CreatureId = body.Entity.EntityId;
        m_eventType = EventType.ApplyImpulse;
        m_Impulse = vector;
    }

    public SubsystemBodyPackage(ComponentBody from, ComponentBody target, Vector3 velocity)
    {
        m_eventType = EventType.HandleAxisCollision;
        CreatureId = from.Entity.EntityId;
        TargetCreatureId = target.Entity.EntityId;
        m_Impulse = velocity;
    }

    private BodyItem ReadItem(PackageStreamReader reader)
    {
        BodyItem bodyItem = new BodyItem();
        bodyItem.changeFlag = reader.ReadEnum<ChangeFlag>();
        bodyItem.CreatureId = reader.ReadUInt16();
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.PositionChange))
        {
            bodyItem.Position = reader.ReadVector3();
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.RotationChange))
        {
            bodyItem.Rotation = reader.ReadQuaternion();
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.VelocityChange))
        {
            bodyItem.Velocity = reader.ReadVector3();
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.LookAnglesChange))
        {
            bodyItem.LookAngles = reader.ReadVector2();
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.FlyOrderChange))
        {
            bodyItem.FlyOrder = reader.ReadVector3Nullable();
        }
        return bodyItem;
    }

    private void WriteItem(PackageStreamWriter writer, BodyItem bodyItem)
    {
        writer.WriteEnum(bodyItem.changeFlag);
        writer.Write(bodyItem.CreatureId);
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.PositionChange))
        {
            writer.Write(bodyItem.Position);
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.RotationChange))
        {
            writer.Write(bodyItem.Rotation);
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.VelocityChange))
        {
            writer.Write(bodyItem.Velocity);
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.LookAnglesChange))
        {
            writer.Write(bodyItem.LookAngles);
        }
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.FlyOrderChange))
        {
            writer.Write(bodyItem.FlyOrder);
        }

    }

    public void ReadData(PackageStreamReader reader)
    {
        m_eventType = reader.ReadEnum<EventType>();
        switch (m_eventType)
        {
            case EventType.BodyUpdate:
                ushort cnt = reader.ReadUInt16();
                for (ushort i = 0; i < cnt; i++)
                {
                    m_bodyList.Add(ReadItem(reader));
                }
                break;
            case EventType.HandleAxisCollision:
                CreatureId = reader.ReadUInt16();
                TargetCreatureId = reader.ReadUInt16();
                m_Impulse = reader.ReadVector3();
                break;
            case EventType.ApplyImpulse:
                CreatureId = reader.ReadUInt16();
                m_Impulse = reader.ReadVector3();
                break;
        }
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_eventType);
        switch (m_eventType)
        {
            case EventType.BodyUpdate:
                writer.Write((ushort)m_bodyList.Count);
                foreach (var i in m_bodyList) WriteItem(writer, i);
                break;
            case EventType.HandleAxisCollision:
                writer.Write(CreatureId);
                writer.Write(TargetCreatureId);
                writer.Write(m_Impulse);
                break;
            case EventType.ApplyImpulse:
                writer.Write(CreatureId);
                writer.Write(m_Impulse);
                break;
        }
    }


        public void Handle(ProjectNet projectNet, bool isServer)
        {
            if (isServer && m_eventType == EventType.BodyUpdate)
            {
                return;
            }
            switch (m_eventType)
            {
                case EventType.BodyUpdate:
                    var bodies = projectNet.FindSubsystem<SubsystemBodies>();
                    List<ushort> ml = new List<ushort>();
                    List<ComponentBody> rl = new List<ComponentBody>();
                    //服务器的动物列表
                    foreach (var item in m_bodyList)
                    {
                        bodies.FindBodyByCreatureID(item.CreatureId, (body) => {
                            if (item.changeFlag.HasFlag(ChangeFlag.PositionChange))
                            {
                                body.netPosition.SetNext(item.Position);
                            }
                            if (item.changeFlag.HasFlag(ChangeFlag.RotationChange))
                            {
                                body.netRotation.SetNext(item.Rotation);
                            }
                            if (item.changeFlag.HasFlag(ChangeFlag.VelocityChange))
                            {
                                body.netVelocity.SetNext(item.Velocity);
                            }
                            if (body.Locomotion != null)
                            {
                                if (item.changeFlag.HasFlag(ChangeFlag.LookAnglesChange))
                                {
                                    body.Locomotion.NetLookAngles.SetNext(item.LookAngles);
                                }
                                if (item.changeFlag.HasFlag(ChangeFlag.FlyOrderChange))
                                {
                                    body.Locomotion.LastFlyOrder = item.FlyOrder;
                                }
                            }
                        }, () => {
                            // 本地没有这个动物，向服务器请求同步。
                            ml.Add(item.CreatureId);
                        });
                    }
                    foreach (var item2 in bodies.Bodies)
                    {
                        BodyItem? m = m_bodyList.Find(x => { return x.CreatureId == item2.Entity.EntityId; });
                        if (!m.HasValue)
                        {
                            rl.Add(item2);
                        }
                    }
                    if (ml.Count > 0)
                    {
                        NetworkManager.QueuePackage(new EntityPackage(ml));
#if DEBUG
                    Log.Information($"客户端向服务器请求同步动物数:{ml.Count}");
#endif
                }
                if (rl.Count > 0)
                {
                    foreach (var b in rl)
                    {
#if DEBUG
                        Log.Information($"客户端移除不同步的实体 {b.Entity.EntityId}");
#endif
                            projectNet.RemoveEntity(b.Entity, true);
                        }
                    }
                    break;
                case EventType.HandleAxisCollision:
                    // 待补全逻辑，外挂可能把玩家撞飞
                    /*
                    projectNet.FindSubsystem<SubsystemBodies>().FindBodyByCreatureID(CreatureId, (from) => {
                        projectNet.FindSubsystem<SubsystemBodies>().FindBodyByCreatureID(TargetCreatureId, (target) => {
                            target.Velocity = m_Impulse;
                            target.netVelocity.SetNext(m_Impulse);
                            from.CollidedWithBody?.Invoke(target);
                            target.CollidedWithBody?.Invoke(from);
                        });
                    });
                    */
                    break;
                case EventType.ApplyImpulse:
                    // 同上
                    /*
                    projectNet.FindSubsystem<SubsystemBodies>().FindBodyByCreatureID(CreatureId, (body) => {
                        body.ApplyImpulseNet(m_Impulse);
                    });
                    */
                    break;
            }

    }
}
