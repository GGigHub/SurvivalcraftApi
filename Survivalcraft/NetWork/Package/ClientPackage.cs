using Engine;

namespace Game.NetWork.Packages;

internal static class ClientPackageSerialization {
    public static void SerializeClientId(PackageSerializer archive, ref byte id) {
        archive.Value(ref id);
    }

    public static void SerializeClient(PackageSerializer archive, Client to, ref Client client) {
        byte id = client?.ID ?? 0;
        Guid tokenId = client?.TokenId ?? Guid.Empty;
        Guid playerGuid = client?.PlayerGuid ?? Guid.Empty;
        string nickname = client?.Nickname ?? string.Empty;

        if (archive.IsWriting && to != null && client != null && to.PlayerGuid == client.PlayerGuid) {
            playerGuid = client.TokenId;
        }

        archive.Value(ref id);
        archive.Value(ref tokenId);
        archive.Value(ref playerGuid);
        archive.Value(ref nickname);

        if (archive.IsReading) {
            client = new Client(null, id, tokenId, playerGuid, nickname);
        }
    }
}

public class ClientAddPackage : IPackage {
    public override byte ID => (byte)PackageType.ClientAdd;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public Client m_client;

    public ClientAddPackage() { }

    public ClientAddPackage(byte id, Guid tokenId, Guid guid, string nickname) {
        m_client = new Client(null, id, tokenId, guid, nickname);
    }

    public override void Serialize(PackageSerializer archive) {
        ClientPackageSerialization.SerializeClient(archive, To, ref m_client);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        NetworkManager.AddClient(new Client(From.Peer, m_client.ID, m_client.TokenId, m_client.PlayerGuid, m_client.Nickname));
    }
}

public class ClientRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.ClientRemove;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public byte m_clientId;

    public ClientRemovePackage() { }

    public ClientRemovePackage(byte id) {
        m_clientId = id;
    }

    public override void Serialize(PackageSerializer archive) {
        ClientPackageSerialization.SerializeClientId(archive, ref m_clientId);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        if (NetworkManager.Clients.ContainsKey(m_clientId)) {
            var client = NetworkManager.Clients[m_clientId];
            client.State = ClientStage.NotConnected;
            NetworkManager.OnClientStateChanged?.Invoke(client);
            NetworkManager.Clients.Remove(m_clientId);
        }
    }
}

public class ClientSyncListPackage : IPackage {
    public override byte ID => (byte)PackageType.ClientSyncList;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public List<Client> m_list = [];

    public ClientSyncListPackage() { }

    public ClientSyncListPackage(IEnumerable<Client> clients) {
        m_list = [.. clients];
    }

    public override void Serialize(PackageSerializer archive) {
        byte count = archive.IsWriting ? (byte)m_list.Count : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_list = [];
            for (int i = 0; i < count; i++) {
                Client client = null;
                ClientPackageSerialization.SerializeClient(archive, To, ref client);
                m_list.Add(client);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                Client client = m_list[i];
                ClientPackageSerialization.SerializeClient(archive, To, ref client);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        foreach (var client in m_list) {
            if (client.ID == 0) {
                client.Peer = From.Peer;
                client.Peer.Tag = client;
                NetworkManager.Client.Server = From;
            }
            else if (client.TokenId == NetworkManager.Client.TokenId) {
                NetworkManager.Client.Self = client;
            }
            NetworkManager.AddClient(client);
        }
        if (NetworkManager.Self == null) {
            throw new Exception("Cannot find Self In Client List");
        }
        NetworkManager.Client.CurrentStage = NetworkStage.Connected;
    }
}

public class ClientStateChangePackage : IPackage {
    public override byte ID => (byte)PackageType.ClientStateChange;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public byte m_clientId;
    public ClientStage m_state;

    public ClientStateChangePackage() { }

    public ClientStateChangePackage(byte id, ClientStage clientStage) {
        m_clientId = id;
        m_state = clientStage;
    }

    public override void Serialize(PackageSerializer archive) {
        ClientPackageSerialization.SerializeClientId(archive, ref m_clientId);
        archive.Enum(ref m_state);
    }

    public override void Handle(bool isServer) {
        if (!NetworkManager.Clients.ContainsKey(m_clientId)) {
            return;
        }
        var client = NetworkManager.Clients[m_clientId];
        if (client.State != m_state) {
            client.State = m_state;
            NetworkManager.OnClientStateChanged?.Invoke(client);
        }
    }
}
