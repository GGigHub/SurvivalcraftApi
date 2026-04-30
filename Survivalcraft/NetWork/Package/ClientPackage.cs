using Engine;

namespace Game.NetWork.Packages;

public class ClientPackage : IPackage {
    public override byte ID => (byte)PackageType.Client;
    public override ClientStage MinNeedState => ClientStage.NotConnected;

    public enum EventType {
        Add,
        Remove,
        SyncList,
        StateChange
    }

    public EventType eventType;
    public Client m_client;
    public List<Client> m_list;

    public ClientPackage() {
    }

    public ClientPackage(byte id, Guid tokenId, Guid guid, string nickname) {
        eventType = EventType.Add;
        m_client = new Client(null, id, tokenId, guid, nickname);
    }

    public ClientPackage(byte id) {
        eventType = EventType.Remove;
        m_client = new Client(null, id, Guid.Empty, Guid.Empty, null);
    }

    public ClientPackage(byte id, ClientStage clientStage) {
        eventType = EventType.StateChange;
        m_client = new Client(null, id, Guid.Empty, Guid.Empty, null) {
            State = clientStage
        };
    }

    public ClientPackage(IEnumerable<Client> clients) {
        m_list = [.. clients];
        eventType = EventType.SyncList;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref eventType);
        switch (eventType) {
            case EventType.Add:
                SerializeClient(archive, ref m_client);
                break;
            case EventType.Remove:
                SerializeClientId(archive, ref m_client);
                break;
            case EventType.SyncList:
                SerializeClientList(archive);
                break;
            case EventType.StateChange:
                SerializeClientId(archive, ref m_client);
                ClientStage state = m_client?.State ?? ClientStage.NotConnected;
                archive.Enum(ref state);
                if (archive.IsReading) {
                    m_client.State = state;
                }
                break;
        }
    }

    private void SerializeClientList(PackageSerializer archive) {
        byte count = archive.IsWriting ? (byte)m_list.Count : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_list = [];
            for (int i = 0; i < count; i++) {
                Client client = null;
                SerializeClient(archive, ref client);
                m_list.Add(client);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                Client client = m_list[i];
                SerializeClient(archive, ref client);
            }
        }
    }

    private void SerializeClientId(PackageSerializer archive, ref Client client) {
        byte id = client?.ID ?? 0;
        archive.Value(ref id);
        if (archive.IsReading) {
            client = new Client(null, id, Guid.Empty, Guid.Empty, null);
        }
    }

    private void SerializeClient(PackageSerializer archive, ref Client client) {
        byte id = client?.ID ?? 0;
        Guid tokenId = client?.TokenId ?? Guid.Empty;
        Guid playerGuid = client?.PlayerGuid ?? Guid.Empty;
        string nickname = client?.Nickname ?? string.Empty;

        archive.Value(ref id);
        archive.Value(ref tokenId);
        archive.Value(ref playerGuid);
        archive.Value(ref nickname);

        if (archive.IsReading) {
            client = new Client(null, id, tokenId, playerGuid, nickname);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer && eventType != EventType.StateChange) {
            return;
        }
        switch (eventType) {
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
                if (NetworkManager.Clients.ContainsKey(m_client.ID)) {
                    var client = NetworkManager.Clients[m_client.ID];
                    client.State = ClientStage.NotConnected;
                    NetworkManager.OnClientStateChanged?.Invoke(client);
                    NetworkManager.Clients.Remove(m_client.ID);
                }
                break;
            case EventType.SyncList:
                foreach (var c in m_list) {
                    if (c.ID == 0) {
                        c.Peer = From.Peer;
                        c.Peer.Tag = c;
                        NetworkManager.Client.Server = From;
                    }
                    else {
                        if (c.TokenId == NetworkManager.Client.TokenId) {
                            NetworkManager.Client.Self = c;
                        }
                    }
                    NetworkManager.AddClient(c);
                }
#if DEBUG
                Log.Information("SyncList End:" + m_list.Count);
#endif
                if (NetworkManager.Self == null) {
                    throw new Exception("Cannot find Self In Client List");
                }
                NetworkManager.Client.CurrentStage = NetworkStage.Connected;
                break;
            case EventType.StateChange:
                if (NetworkManager.Clients.ContainsKey(m_client.ID)) {
                    From = NetworkManager.Clients[m_client.ID];
                    if (From.State != m_client.State) {
                        From.State = m_client.State;
                        NetworkManager.OnClientStateChanged?.Invoke(From);
                    }
                }
                break;
        }
    }
}
