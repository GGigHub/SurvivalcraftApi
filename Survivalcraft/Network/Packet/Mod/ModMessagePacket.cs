namespace Game.Network;

public sealed class ModMessagePacket : Packet {
    public override byte ID => (byte)PacketType.ModPacket;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public ModNetworkEnvelope Envelope = new();

    public ModMessagePacket() {
    }

    public ModMessagePacket(ModNetworkEnvelope envelope) {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
    }

    public override void Serialize(PacketSerializer serializer) {
        byte[] payload = serializer.IsWriting
            ? ModNetworkProtocol.EncodeEnvelope(Envelope)
            : null;
        serializer.Buffer(ref payload, ModNetworkProtocol.MaximumEnvelopeEncodedBytes);
        if (serializer.IsReading) {
            Envelope = ModNetworkProtocol.DecodeEnvelope(payload);
        }
    }

    public override void Handle(bool isServer) {
        _ = ModNetworkManager.Dispatch(Envelope, isServer, From);
    }
}
