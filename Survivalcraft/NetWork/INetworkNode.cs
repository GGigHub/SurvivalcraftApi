using System.Net;
using Game.NetWork.Packages;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

public interface INetworkNode {
    NetManager NetManager { get; }
    Client Self { get; set; }
    Client Server { get; set; }
    NetworkStage CurrentStage { get; set; }
    bool IsServer { get; }
    int ClientCount { get; }
    Dictionary<int, Client> Clients { get; }
    IEnumerable<Client> Peers { get; }
    List<IPackage> PendingPackages { get; }
    List<IPackage> PendingHandlePackages { get; }
    Action<Client> OnClientStateChanged { get; set; }
    Guid TokenId { get; set; }
    bool isStopping { get; set; }
    WorkType WorkType { get; set; }

    void AddClient(Client c);
    void RemoveClientImmediate(Client c, string reason = null);
    Client GetClientByID(byte id);
    Client GetClientByGUID(Guid? guid);
    Client GetClientByTokenID(Guid? guid);
    void StartLocal();
    void SendWriterFromPackage(IPackage package, IPEndPoint endPoint, bool isSend = true);
    void SendWriterFromPackage(IPackage package, NetPeer netPeer, bool useDeliveryEvent = false);
    void SendWriterFromPackage(IPackage package, ConnectionRequest request, bool reject);
    void SendWriterFromPackages(IEnumerable<IPackage> packages, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false);
    void Stop();
    void StopImmediate();
    void Update();
    void CleanupAfterStop();
    byte[] BuildOutgoingPayload(IEnumerable<IPackage> packages, bool isServer);
    void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false);
    void CleanupChunkStates();
    bool TryDecodePackages(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<IPackage> packages);
}
