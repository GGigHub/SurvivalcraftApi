using System;

namespace Game.Network;

public sealed class PlayerEntityRequestPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerEntityRequest;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int PlayerIndex;

    public PlayerEntityRequestPacket() {
    }

    public PlayerEntityRequestPacket(int playerIndex) {
        PlayerIndex = playerIndex;
    }

    public override void Serialize(PacketSerializer serializer) {
        serializer.Value(ref PlayerIndex);
    }

    public override void Handle(bool isServer) {
        if (!isServer
            || From == null
            || PlayerIndex < 0
            || PlayerIndex != From.PlayerIndex
            || From.NetworkState < NetworkState.ProjectLoaded
            || GameManager.Project == null) {
            return;
        }
        DateTime now = DateTime.UtcNow;
        if (From.LastPlayerEntityRequestUtc != default
            && now - From.LastPlayerEntityRequestUtc < TimeSpan.FromSeconds(1)) {
            return;
        }
        From.LastPlayerEntityRequestUtc = now;

        SubsystemNetwork subsystemNetwork =
            GameManager.Project.FindSubsystem<SubsystemNetwork>();
        PlayerData playerData = From.GetPlayerData();
        if (playerData == null) {
            return;
        }
        if (playerData.ComponentPlayer == null) {
            subsystemNetwork.MakePlayerEntityOnline(From, out _);
            playerData = From.GetPlayerData();
        }
        if (playerData?.ComponentPlayer != null) {
            NetworkManager.Queue(new EntityLoadPacket(playerData.ComponentPlayer.Entity) {
                To = From
            });
        }
    }
}
