using System.Net;
using Game.Network;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public interface INetworkNode {
    NetManager NetManager { get; }
    Client Self { get; set; }
    Client Server { get; set; }
    NetworkStage CurrentStage { get; set; }
    bool IsServer { get; }
    int ClientCount { get; }
    Dictionary<int, Client> Clients { get; }
    IEnumerable<Client> Peers { get; }
    List<Packet> PendingPackets { get; }
    List<Packet> PendingHandlePackets { get; }
    Action<Client> OnClientStateChanged { get; set; }
    Guid TokenId { get; set; }
    WorkType WorkType { get; set; }

    void AddClient(Client c);
    void RemoveClientImmediate(Client c, string reason = null);
    Client GetClientByID(byte id);
    Client GetClientByGUID(Guid? guid);
    Client GetClientByTokenID(Guid? guid);
    void StartLocal();
    void SendWriterFromPacket(Packet packet, IPEndPoint endPoint, bool isSend = true);
    void SendWriterFromPacket(Packet packet, NetPeer netPeer, bool useDeliveryEvent = false);
    void SendWriterFromPacket(Packet packet, ConnectionRequest request, bool reject);
    void SendWriterFromPackets(IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false);
    void Stop();
    void Update();
    byte[] BuildOutgoingPayload(IEnumerable<Packet> packets, bool isServer);
    void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false);
    void CleanupChunkStates();
    bool TryDecodePackets(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<Packet> packets);
}


