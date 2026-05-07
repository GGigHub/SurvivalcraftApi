

namespace Game.Network;

public class ConnectionRejectPacket : Packet {
    public override byte ID => (byte)PacketType.ConnectReject;
    public override ClientStage MinNeedState => ClientStage.NotConnected;

    public string reason;

    public ConnectionRejectPacket() {
    }

    public ConnectionRejectPacket(string r) {
        reason = r;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref reason);
    }

    public override void Handle(bool isServer) {
        NetworkManager.Stop();
    }
}

