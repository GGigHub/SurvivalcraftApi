using System;
using System.Net;
using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib;

namespace Game.NetWork;

public class Client {
    public bool IsLocalRemote { get; set; }

    public string Nickname { get; set; }

    public byte ID { get; internal set; }

    // 客户端获取到其他玩家的PlayerGuid是他们各自的TokenID，被混淆过了
    // 客户端自己的PlayerGuid，以及服务端上所有玩家的PlayerGuid都未经混淆
    // 以上的PlayerGuid指的是ComponentPlayer,PlayerData,Client中的PlayerGuid
    public Guid PlayerGuid { get; internal set; }

    public Guid TokenId { get; internal set; }

    public NetPeer Peer { get; internal set; }

    public ConnectionRequest Request { get; internal set; }

    public IPEndPoint IPPoint { get; internal set; }

    public SubsystemPlayers SubsystemPlayers => GameManager.Project.FindSubsystem<SubsystemPlayers>();

    public bool IsConnected => Peer != null && Peer.ConnectionState != ConnectionState.Disconnected;

    public string CommunityAccountId { get; set; } = "-1";

    public ClientState State { get; set; }

    public PlayerData PlayerData { get { return SubsystemPlayers.PlayersData.Find(p => p.ClientId == ID); } }

    public Entity CachePlayerEntity { get; set; }

    /// <summary>
    /// 是否在连接成功列表
    /// </summary>
    public bool IsAddedToNetNode { get; private set; }

    //服务器本身构造
    public Client(Guid guid) {
        PlayerGuid = guid;
        State = ClientState.Connected;
        TokenId = Guid.NewGuid();
        ID = 0;
    }

    /// <summary>
    /// 服务端创建新的Client
    /// </summary>
    /// <param name="netPeer">peer</param>
    /// <param name="id">客户端ID</param>
    /// <param name="tokenId">客户端tokenId</param>
    /// <param name="guid">客户端Guid</param>
    /// <param name="dataId">社区账号ID</param>
    /// <param name="nickName">社区昵称</param>
    public Client(NetPeer netPeer, byte id, Guid tokenId, Guid guid, string dataId, string nickName) {
        ID = id;
        PlayerGuid = guid;
        TokenId = tokenId;
        Nickname = nickName;
        CommunityAccountId = dataId;
        Peer = netPeer;
        IsAddedToNetNode = true;
    }

    public Client(IPEndPoint iPEndPoint) {
        IPPoint = iPEndPoint;
        State = ClientState.NotConnected;
    }
    public Client(ConnectionRequest request) {
        Request = request;
        IPPoint = request.RemoteEndPoint;
        State = ClientState.NotConnected;
    }
    public Client(NetPeer peer) {
        Peer = peer;
        State = ClientState.NotConnected;
        TokenId = Guid.NewGuid();
    }

    public override bool Equals(object obj) => obj is Client c && c.ID == ID;

    public override int GetHashCode() => PlayerGuid.GetHashCode();

    public static bool operator ==(Client a, Client b) {
        return a is null && b is null || !(a is null) && !(b is null) && a.ID == b.ID;
    }

    public static bool operator !=(Client a, Client b) => !(a == b);

    public override string ToString() => $"Client : {{ID : {ID}, GUID : {PlayerGuid}}}";

}


public class NetworkServer : NetworkBase
{
    public override bool StartServer(int port)
    {
        if (netManager.IsRunning)
        {
            StopImmediate();
        }
        try
        {
            isStopping = false;
            StartLocal();
            if (netManager.Start(port))
            {
                Log.Information($"Server started successfully on port {netManager.LocalPort}");
                Window.Frame += Update;
                Window.Closed += StopImmediate;
                listener.ConnectionRequestEvent += ConnectionRequestEvent;
                listener.NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
                listener.NetworkReceiveEvent += NetworkReceiveEvent;
                listener.PeerDisconnectedEvent += PeerDisconnectedEvent;
                netManager.PollEvents();
                CurrentStage = Stage.Connected;
                Server = Self;
                WorkType = WorkType.Server;
            }
            else
            {
                CurrentStage = Stage.NotConnected;
                Stop();
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
        return CurrentStage == Stage.Connected;
    }

    public override void DeliveryEvent(NetPeer peer, object userData)
    {
        if (userData is NetPeer netPeer && netPeer.Tag is Client client)
        {
            agreeOnPendingPeer.Remove(client.ID);
#if DEBUG
            Log.Information($"Client[{client.ID}] acknowledged pending client [{PendingClient.ID}]");
#endif
        }
        if (agreeOnPendingPeer.Count == 0 && PendingClient != null)
        {
            AddClient(PendingClient);
            QueuePackage(new ClientPackage(Clients.Values) { To = PendingClient });
#if DEBUG
            Log.Information($"Client[{PendingClient.ID}] join flow completed.");
#endif
            pendingPeer = null;
            pendingStartUtc = null;
        }
    }

    protected override void ConnectionRequestEvent(ConnectionRequest request)
    {
        Log.Information("Received connection request " + request.RemoteEndPoint);

        if (ProjectNet.Project == null)
        {
            SendWriterFromPackage(new ConnectionRejectPackage("Please wait until server startup is complete before joining."), request, true);
        }
        else if (PendingClient != null)
        {
            Log.Information($"Too Many Request And Reject Current Request:{request.RemoteEndPoint}");
            Log.Information($"Current Pending Client:{PendingClient}");
            SendWriterFromPackage(new ConnectionRejectPackage($"Server is handling another pending join. Waiting client confirmations: {agreeOnPendingPeer.Count}"), request, true);
        }
        else
        {
            try
            {
                ConnectionRequestPackage requestPackage = PackageManager.DecodePackage<ConnectionRequestPackage>(this, request.Data, null, request);
                requestPackage.Handle(ProjectNet.Project, this, true);
            }
            catch (Exception e)
            {
                SendWriterFromPackage(new ConnectionRejectPackage($"Invalid connection request packet: {e.Message}"), request, true);
                Log.Information("ConnectionRequestEvent Error:" + e.StackTrace);
            }
        }
    }
}
