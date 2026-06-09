using System;
using System.Collections.Generic;
using System.Text;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ConnectPacket : Packet {
    public override byte ID => (byte)PacketType.ConnectInfo;

    public Guid playerGuid;
    public ConnectPacket() {
    }

    public ConnectPacket(Guid guid) {
        playerGuid = guid;
    }

    public override void Serialize(PacketSerializer serializer) {
        serializer.Value(ref playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            // 前置拦截如校验黑名单
            From.NetworkGuid = playerGuid;
            // 向客户端发送project
        }
    }
}
