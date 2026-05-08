using System.Net;
using Engine;
using LiteNetLib;

namespace Game.Network;

public class Client {
    public bool IsLocalRemote { get; set; }
    public string Nickname { get; set; }
    public byte ID { get; internal set; }
    // PlayerGuid is the unique identifier for each player, used consistently across Client and Server.
    public Guid PlayerGuid { get; internal set; }
    public NetPeer Peer { get; internal set; }
    public ConnectionRequest Request { get; internal set; }
    public IPEndPoint IPPoint { get; internal set; }
    public SubsystemPlayers SubsystemPlayers => GameManager.Project.FindSubsystem<SubsystemPlayers>();
    public bool IsConnected => Peer != null && Peer.ConnectionState != ConnectionState.Disconnected;
    public ClientStage State { get; set; }
    public PlayerData PlayerData { get { return SubsystemPlayers.PlayersData.Find(p => p.ClientId == ID); } }

    /// <summary>
    /// 是否已加入连接成功列表
    /// </summary>
    public bool IsAddedToNetNode { get; private set; }
    
    // 服务器本身的Client
    public Client(Guid guid) {
        PlayerGuid = guid;
        State = ClientStage.Connected;
        ID = 0;
    }

    /// <summary>
    /// 服务端创建新的Client
    /// </summary>
    /// <param name="netPeer">peer</param>
    /// <param name="id">客户端ID</param>
    /// <param name="guid">客户端Guid</param>
    /// <param name="nickName">社区昵称</param>
    public Client(NetPeer netPeer, byte id, Guid guid, string nickName) {
        ID = id;
        PlayerGuid = guid;
        Nickname = nickName;
        Peer = netPeer;
        IsAddedToNetNode = true;
    }

    public Client(IPEndPoint iPEndPoint) {
        IPPoint = iPEndPoint;
        State = ClientStage.NotConnected;
    }

    public Client(ConnectionRequest request) {
        Request = request;
        IPPoint = request.RemoteEndPoint;
        State = ClientStage.NotConnected;
    }

    public Client(NetPeer peer) {
        Peer = peer;
        State = ClientStage.NotConnected;
    }

    public override bool Equals(object obj) => obj is Client c && c.ID == ID;

    public override int GetHashCode() => PlayerGuid.GetHashCode();

    public static bool operator ==(Client a, Client b) {
        return (a is null && b is null) || (a is not null && b is not null && a.ID == b.ID);
    }

    public static bool operator !=(Client a, Client b) => !(a == b);
}
