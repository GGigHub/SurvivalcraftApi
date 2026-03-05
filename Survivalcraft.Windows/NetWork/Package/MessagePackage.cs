using System.Collections.Generic;

namespace Game.NetWork.Packages;

public class MessagePackage : IPackage
{

    public byte ID => (byte)PackageType.Message;
    public enum MessageMode : byte
    {
        BaseMessage,                        // 普通消息
        LargeMessage                        // 大字消息，也就是开局显示的那几条消息
    }

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public string m_message;
    public string m_playerName;
    public byte m_messageType = 0;
    public List<byte> m_toClients = [];


    public string m_largeText;
    public string m_smallText;
    public float m_duration;
    public float m_delay;

    public MessageMode m_messageMode;
    public MessagePackage()
    {
    }
    public MessagePackage(string playerName, string Message, byte type, List<byte> toClients)
    {
        m_messageMode = MessageMode.BaseMessage;
        m_message = Message;
        m_playerName = playerName;
        m_messageType = type;
        if (toClients != null) m_toClients.AddRange(toClients);
    }

    // 发送大字消息，可以做服务器公告用
    public MessagePackage(string largeText, string smallText, float duration, float delay)
    {
        m_messageMode = MessageMode.LargeMessage;
        m_largeText = largeText == null ? "" : largeText;
        m_smallText = smallText == null ? "" : smallText;
        m_duration = duration;
        m_delay = delay;
    }
    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_messageMode);
        switch (m_messageMode)
        {
            case MessageMode.BaseMessage:
                writer.Write(m_messageType);
                writer.Write((byte)m_toClients.Count);
                for (byte i = 0; i < m_toClients.Count; i++)
                {
                    writer.Write(m_toClients[i]);
                }
                writer.Write(m_message);
                if (m_playerName == null) writer.Write(false);          // 这里不要布尔判断，而是直接写入string.Empty可能会好一点
                else
                {
                    writer.Write(true);
                    writer.Write(m_playerName);
                }
                break;
            case MessageMode.LargeMessage:
                writer.Write(m_largeText);
                writer.Write(m_smallText);
                writer.Write(m_duration);
                writer.Write(m_delay);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_messageMode = reader.ReadEnum<MessageMode>();
        switch (m_messageMode)
        {
            case MessageMode.BaseMessage:
                m_messageType = reader.ReadByte();
                byte count = reader.ReadByte();
                for (int i = 0; i < count; i++) { m_toClients.Add(reader.ReadByte()); }
                m_message = reader.ReadString();
                if (reader.ReadBoolean())
                {
                    m_playerName = reader.ReadString();
                }
                break;
            case MessageMode.LargeMessage:
                m_largeText = reader.ReadString();
                m_smallText = reader.ReadString();
                m_duration = reader.ReadSingle();
                m_delay = reader.ReadSingle();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        switch (m_messageMode)
        {
            case MessageMode.BaseMessage:
                {
                    var m_gameWidgets = projectNet.FindSubsystem<SubsystemGameWidgets>();
                    bool External = false;
                    m_gameWidgets.AddNetMessage(m_message, m_playerName, m_messageType, m_toClients, External);
                    if (isServer)
                    {
                        this.m_playerName = From.PlayerData.Name;

                        bool flag = projectNet.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Contains(From.PlayerGuid.ToString());
                        if (!flag)
                        {
                            Except = From;
                            netNode.QueuePackage(this);
                        }
                    }
                }
                break;
            case MessageMode.LargeMessage:
                if (isServer == false)
                {
                    foreach (var player in projectNet.FindSubsystem<SubsystemPlayers>().PlayersData)
                    {
                        player.ComponentPlayer?.ComponentGui?.DisplayLargeMessage(m_largeText, m_smallText, m_duration, m_delay);
                    }
                }
                else
                {
                    // 客户端给服务端发大字????
                }
                break;
        }
    }

    public void InsertGroupClients(List<byte> clients)
    {
    }

}
