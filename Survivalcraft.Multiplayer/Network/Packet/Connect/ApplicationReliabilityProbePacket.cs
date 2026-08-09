namespace Game.Network;

/// <summary>
/// A side-effect-free payload used by deterministic fault tests to exercise the
/// complete application-reliable data, acknowledgement and retry path even when
/// a scenario otherwise emits only replaceable state streams.
/// </summary>
public sealed class ApplicationReliabilityProbePacket : Packet {
    private long nonce;

    public override byte ID => (byte)PacketType.ApplicationReliabilityProbe;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public override bool RequiresProject => false;
    public long Nonce => nonce;

    public ApplicationReliabilityProbePacket() {
    }

    public ApplicationReliabilityProbePacket(long nonce) {
        this.nonce = nonce;
        Validate();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref nonce);
        Validate();
    }

    public override void Handle(bool isServer) {
        // Delivery and deduplication are intentionally owned by the enclosing
        // ApplicationReliablePacket. The probe has no gameplay side effects.
    }

    private void Validate() {
        if (nonce <= 0) {
            throw new InvalidDataException(
                "Application-reliability probe nonce must be positive.");
        }
    }
}
