using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameEntitySystem;
using Engine;

namespace Game.NetWork.Packages;

public class ComponentOnFirePackage : IPackage
{
    public byte ID => (byte)PackageType.ComponentOnFire;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public ushort m_entityId;
    public ushort m_attackerEntityId;
    public float m_duration;
    public int x, y, z;

    public enum EventType
    {
        ComponentOnFire,
        BlockOnFireAdd,
        BlockOnFireRemove
    }
    public EventType m_type;

    public ComponentOnFirePackage()
    {
    }
    public ComponentOnFirePackage(ComponentOnFire target, ComponentCreature attacker, float duration)
    {
        m_type = EventType.ComponentOnFire;
        m_duration = duration;
        if (attacker != null) m_attackerEntityId = attacker.Entity.EntityId;
        m_entityId = target.Entity.EntityId;
    }
    public ComponentOnFirePackage(int x, int y, int z, float expandability)
    {
        m_type = EventType.BlockOnFireAdd;
        this.x = x;
        this.y = y;
        this.z = z;
        m_duration = expandability;
    }
    public ComponentOnFirePackage(int x, int y, int z)
    {
        m_type = EventType.BlockOnFireRemove;
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case EventType.ComponentOnFire:
                writer.Write(m_entityId);
                writer.Write(m_attackerEntityId);
                writer.Write(m_duration);
                break;
            case EventType.BlockOnFireAdd:
                writer.Write(x);
                writer.Write(y);
                writer.Write(z);
                writer.Write(m_duration);
                break;
            case EventType.BlockOnFireRemove:
                writer.Write(x);
                writer.Write(y);
                writer.Write(z);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<EventType>();
        switch (m_type)
        {
            case EventType.ComponentOnFire:
                m_entityId = reader.ReadUInt16();
                m_attackerEntityId = reader.ReadUInt16();
                m_duration = reader.ReadSingle();
                break;
            case EventType.BlockOnFireAdd:
                x = reader.ReadInt32();
                y = reader.ReadInt32();
                z = reader.ReadInt32();
                m_duration = reader.ReadSingle();
                break;
            case EventType.BlockOnFireRemove:
                x = reader.ReadInt32();
                y = reader.ReadInt32();
                z = reader.ReadInt32();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (isServer)
        {
            return;
        }
        switch (m_type)
        {
            case EventType.BlockOnFireAdd:
                projectNet.FindSubsystem<SubsystemFireBlockBehavior>().AddFireNet(x, y, z, m_duration);
                break;
            case EventType.BlockOnFireRemove:
                projectNet.FindSubsystem<SubsystemFireBlockBehavior>().RemoveFireNet(x, y, z);
                break;
            case EventType.ComponentOnFire:
                projectNet.FindEntityById(m_entityId, (e) =>
                {
                    ComponentOnFire onFire = e.FindComponent<ComponentOnFire>();
                    if (onFire != null)
                    {
                        if (m_attackerEntityId == 0) onFire.SetOnFireNet(null, m_duration);
                        else
                        {
                            projectNet.FindEntityById(m_attackerEntityId, (e2) =>
                            {
                                ComponentCreature creature = e2.FindComponent<ComponentCreature>();
                                onFire.SetOnFireNet(creature, m_duration);
                            });
                        }
                    }
                });
                break;
        }
    }
}
