using Engine;
using GameEntitySystem;

namespace Game.NetWork.Packages;

public class ComponentSleepPackage : IPackage
{
    public byte ID => (byte)PackageType.ComponentSleep;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public ushort m_entityId;
    public string reason = "";

    public enum EventType
    {
        SleepRequest,
        Sleep,
        WakeupRequest,
        WakeUp
    }

    public EventType m_type;
    public bool m_result;
    public bool allowManualWakeup;

    public ComponentSleepPackage()
    {
    }
    public ComponentSleepPackage(ComponentSleep sleep, EventType eventType, bool allow = false, bool result = false, string msg = "")
    {
        reason = msg;
        m_entityId = sleep.Entity.EntityId;
        m_type = eventType;
        allowManualWakeup = allow;
        m_result = result;
    }
    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(m_entityId);
        writer.WriteEnum(m_type);
        writer.Write(allowManualWakeup);
        writer.Write(m_result);
        writer.Write(reason);
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_entityId = reader.ReadUInt16();
        m_type = reader.ReadEnum<EventType>();
        allowManualWakeup = reader.ReadBoolean();
        m_result = reader.ReadBoolean();
        reason = reader.ReadString();
    }

    public void Handle(ProjectNet projectNet, NetworkBase networkBase, bool isServer)
    {
        projectNet.FindEntityById(m_entityId, (e) =>
        {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            if (sleep == null) return;
            ComponentPlayer player = e.FindComponent<ComponentPlayer>();
            if (player == null) return;
            if (isServer && player.PlayerGuid != From.PlayerGuid)
            {
                return;
            }
            switch (m_type)
            {
                case EventType.SleepRequest:
                    if (sleep.CanSleep(out string reason2))
                    {
                        sleep.Sleep(allowManualWakeup);
                    }
                    else
                    {
                        networkBase.QueuePackage(new ComponentSleepPackage(sleep, EventType.Sleep, allowManualWakeup, false, reason2) { To = From });
                    }
                    break;
                case EventType.Sleep:
                    if (m_result) sleep.NetSleep(allowManualWakeup);
                    else
                    {
                        var player1 = sleep.Entity.FindComponent<ComponentPlayer>();
                        player1.ComponentGui.DisplaySmallMessage(reason, Color.White, false, true);
                    }
                    break;
                case EventType.WakeupRequest:
                    sleep.WakeUp();
                    break;
                case EventType.WakeUp:
                    sleep.NetWakeUp();
                    break;
            }
        });
    }
}

