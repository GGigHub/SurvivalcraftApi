using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using LiteNetLib;

namespace Game.Network;

public class ClientSession {
    // 不是ClientIndex
    public int PlayerIndex { get; set; }
    public Guid NetworkGuid { get; set; }
    public NetworkState NetworkState { get; set; }
}

interface INetworkClient {
    // 我们使用PlayerIndex来存档内辨识玩家，客户端不该拥有ClientSession对象
    bool Connect();
    void Disconnect();
    void Update();
    void Send(Packet packet);
}

interface INetworkServer {
    bool Start();
    void Stop();
    void Update();
    void Board(Packet packet);
    void Send(Packet packet, int playerIndex);
}
