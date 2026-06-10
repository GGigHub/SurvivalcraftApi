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
            SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            if (subsystemPlayers.MakePlayerOnline(From.PlayerIndex, out PlayerData playerData, out Entity entity)) {
                if (entity != null) {
                    GameManager.Project.AddEntity(entity);
                }
                From.PlayerIndex = playerData.PlayerIndex;
                NetworkService.QueuePacket(new PlayerAddPacket(playerData) { Except = From });
            }
            byte[] data = NetworkService.GetNowProject(GameManager.Project);
            NetworkService.QueuePacket(new ProjectPacket(data, From.PlayerIndex) { To = From });
        }
    }
}
