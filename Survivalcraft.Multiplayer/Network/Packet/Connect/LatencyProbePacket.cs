namespace Game.Network;

public class LatencyProbePacket : Packet {
    public override byte ID => (byte)PacketType.LatencyProbe;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public int Sequence;
    public double ClientSentTime;
    public bool IsReply;

    public LatencyProbePacket() { }

    public LatencyProbePacket(int sequence, double clientSentTime, bool isReply) {
        Sequence = sequence;
        ClientSentTime = clientSentTime;
        IsReply = isReply;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref ClientSentTime);
        archive.Value(ref IsReply);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            if (IsReply || From == null) return;
            NetworkManager.Queue(new LatencyProbePacket(Sequence, ClientSentTime, true) { To = From });
            return;
        }
        if (IsReply) {
            LatencyProbe.ReceiveReply(Sequence, ClientSentTime);
        }
    }
}
