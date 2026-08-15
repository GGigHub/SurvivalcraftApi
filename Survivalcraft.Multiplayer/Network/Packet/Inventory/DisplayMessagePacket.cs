using Engine;

namespace Game.Network;

public class DisplayMessagePacket : Packet {
    public override byte ID => (byte)PacketType.DisplayMessage;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int playerEntityId;
    public string message = "";

    public DisplayMessagePacket() {
    }

    public DisplayMessagePacket(int playerEntityId, string message) {
        this.playerEntityId = playerEntityId;
        this.message = message ?? string.Empty;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref playerEntityId);
        archive.Value(ref message);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(playerEntityId, e => {
            ComponentPlayer player = e.FindComponent<ComponentPlayer>();
            player?.ComponentGui.DisplaySmallMessage(message, Color.White, true, true);
        });
    }
}
