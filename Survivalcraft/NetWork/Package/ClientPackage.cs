using Engine;
using System;
using System.Collections.Generic;

namespace Game.NetWork.Packages;

/// <summary>
/// 基础包模板复�?
/// </summary>
public class ClientPackage : IPackage
{
    public byte ID => (byte)PackageType.Client;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.NotConnected;
    public enum EventType
    {
        Add,
        Remove,
        SyncList,
        StateChange
    }
    public EventType eventType;
    public Client m_client;
    public List<Client> m_list;

    public ClientPackage() { }

    public ClientPackage(byte id, Guid tokenId, Guid guid, string nickname)
    {
        eventType = EventType.Add;
        m_client = new Client(null, id, tokenId, guid, nickname);
    }

    public ClientPackage(byte id)
    {
        eventType = EventType.Remove;
        m_client = new Client(null, id, Guid.Empty, Guid.Empty, null);
    }
    public ClientPackage(byte id, ClientStage ClientStage)
    {
        eventType = EventType.StateChange;
        m_client = new Client(null, id, Guid.Empty, Guid.Empty, null)
        {
            State = ClientStage
        };
    }
    public ClientPackage(IEnumerable<Client> clients)
    {
        m_list = [.. clients];
        eventType = EventType.SyncList;
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (isServer && eventType != EventType.StateChange)
        {
            return;
        }
        switch (eventType)
        {
            case EventType.Add:
                NetworkManager.AddClient(new Client(From.Peer, m_client.ID, m_client.TokenId, m_client.PlayerGuid, m_client.Nickname));
#if DEBUG
                Log.Information("AddClient:" + m_client.ID);
#endif
                break;
            case EventType.Remove:
#if DEBUG
                Log.Information("RemoveClient:" + m_client.ID);
#endif
                if (NetworkManager.Clients.ContainsKey(m_client.ID))
                {
                    var client = NetworkManager.Clients[m_client.ID];
                    client.State = ClientStage.NotConnected;
                    NetworkManager.OnClientStateChanged?.Invoke(client);
                    NetworkManager.Clients.Remove(m_client.ID);
                }
                break;
            case EventType.SyncList:
                foreach (var c in m_list)
                {
                    if (c.ID == 0)
                    {
                        c.Peer = From.Peer;
                        c.Peer.Tag = c;
                        NetworkManager.Client.Server = From;
                    }
                    else
                    {
                        if (c.TokenId == NetworkManager.Client.TokenId) NetworkManager.Client.Self = c;
                    }
                    NetworkManager.AddClient(c);
                }
#if DEBUG
                Log.Information("SyncList End:" + m_list.Count);
#endif
                if (NetworkManager.Self == null) throw new Exception("Cannot find Self In Client List");
                NetworkManager.Client.CurrentStage = NetworkBase.Stage.Connected;
                break;
            case EventType.StateChange:
                if (NetworkManager.Clients.ContainsKey(m_client.ID))
                {
                    From = NetworkManager.Clients[m_client.ID];
                    if (From.State != m_client.State)
                    {
                        From.State = m_client.State;
                        NetworkManager.OnClientStateChanged?.Invoke(From);
                    }
                }
                break;
        }
    }

    public void ReadData(PackageStreamReader reader)
    {
        eventType = reader.ReadEnum<EventType>();
        switch (eventType)
        {
            case EventType.Add:
                m_client = ReadItem(reader);
                break;
            case EventType.Remove:
                m_client = new Client(null, reader.ReadByte(), Guid.Empty, Guid.Empty, null);
                break;
            case EventType.SyncList:
                m_list = [];
                byte count = reader.ReadByte();
                for (int i = 0; i < count; i++)
                {
                    m_list.Add(ReadItem(reader));
                }
                break;
            case EventType.StateChange:
                m_client = new Client(null, reader.ReadByte(), Guid.Empty, Guid.Empty, null)
                {
                    State = reader.ReadEnum<ClientStage>()
                };
                break;
        }

    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(eventType);
        switch (eventType)
        {
            case EventType.Add:
                WriteItem(writer, m_client);
                break;
            case EventType.Remove:
                writer.Write(m_client.ID);
                break;
            case EventType.SyncList:
                writer.Write((byte)m_list.Count);
                foreach (var c in m_list) WriteItem(writer, c);
                break;
            case EventType.StateChange:
                writer.Write(m_client.ID);
                writer.WriteEnum(m_client.State);
                break;
        }
    }

    private void WriteItem(PackageStreamWriter writer, Client client)
    {
        writer.Write(client.ID);
        writer.Write(client.TokenId);

        if ((To == null || To.PlayerGuid != client.PlayerGuid))
            writer.Write(client.TokenId);
        else
            writer.Write(client.PlayerGuid);

        writer.Write(client.Nickname ?? string.Empty);
    }
    private Client ReadItem(PackageStreamReader reader)
    {
        return new Client(null, reader.ReadByte(), reader.ReadGuid(), reader.ReadGuid(), reader.ReadString());
    }
}
