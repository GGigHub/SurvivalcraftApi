using Engine;

namespace Game.NetWork.Packages;

public class ComponentMountPackage : IPackage
{
    public byte ID => (byte)PackageType.ComponentMount;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public ushort m_targetId;
    public ushort m_fromId;

    public EventType m_type;

    public enum EventType
    {
        Mount,
        Dismount,
        MountRequest,
        DismountRequest
    }

    public ComponentMountPackage()
    {
    }
    public ComponentMountPackage(ComponentRider rider, ComponentMount mount, bool isRequest = false)
    {
        m_type = isRequest ? EventType.MountRequest : EventType.Mount;
        m_fromId = rider.Entity.EntityId;
        m_targetId = mount.Entity.EntityId;
    }
    public ComponentMountPackage(ComponentRider rider, bool isRequest = false)
    {
        m_type = isRequest ? EventType.DismountRequest : EventType.Dismount;
        m_fromId = rider.Entity.EntityId;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_type);
        writer.Write(m_fromId);
        writer.Write(m_targetId);
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<EventType>();
        m_fromId = reader.ReadUInt16();
        m_targetId = reader.ReadUInt16();
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        if (isServer && (m_type != EventType.MountRequest && m_type != EventType.DismountRequest))
        {
            return;
        }
        switch (m_type)
        {
            case EventType.Dismount:
                projectNet.FindEntityById(m_fromId, (entity) =>
                {
                    var rider = entity.FindComponent<ComponentRider>();
                    //rider是骑乘者
                    rider?.StartNetDismounting();
                    if (!isServer && rider.Mount != null)
                    {
                        //禁用骑乘生物的组件行为
                        var select = rider.Mount.Entity.FindComponent<ComponentBehaviorSelector>();
                        if (select != null) select.IsDisableBehavior = true;
                    }
                });
                break;
            case EventType.DismountRequest:
                projectNet.FindEntityById(m_fromId, (entity) =>
                {
                    var rider = entity.FindComponent<ComponentRider>();
                    rider?.StartDismounting();
                });
                break;
            case EventType.Mount:
                projectNet.FindEntityById(m_fromId, (entity) =>
                {
                    var rider = entity.FindComponent<ComponentRider>();
                    projectNet.FindEntityById(m_targetId, (entity2) =>
                    {
                        var mount = entity2.FindComponent<ComponentMount>();
                        if (mount != null && rider != null)
                        {
                            rider.StartNetMounting(mount);
                            if (!isServer)
                            {
                                //启动骑乘生物的组件行为
                                var select = rider.Mount.Entity.FindComponent<ComponentBehaviorSelector>();
                                if (select != null) select.IsDisableBehavior = false;
                            }
                        }
                    });
                });
                break;
            case EventType.MountRequest:
                projectNet.FindEntityById(m_fromId, (entity) =>
                {
                    var rider = entity.FindComponent<ComponentRider>();
                    projectNet.FindEntityById(m_targetId, (entity2) =>
                    {
                        var mount = entity2.FindComponent<ComponentMount>();
                        if (mount != null && rider != null)
                        {
                            rider.StartMounting(mount);
                        }
                    });
                });
                break;
        }
    }
}

