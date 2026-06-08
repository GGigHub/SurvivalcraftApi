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
