using System;
using Engine;

namespace Game.Network;

public sealed class CompatibilityHelloPacket : Packet {
    public override byte ID => (byte)PacketType.CompatibilityHello;

    public NetworkCompatibilityManifest Manifest = new();

    public CompatibilityHelloPacket() {
    }

    public CompatibilityHelloPacket(NetworkCompatibilityManifest manifest) {
        Manifest = manifest?.Clone() ?? new NetworkCompatibilityManifest();
    }

    public override void Serialize(PacketSerializer serializer) {
        byte[] payload = serializer.IsWriting
            ? NetworkCompatibilityWireCodec.EncodeManifest(Manifest)
            : null;
        serializer.Buffer(ref payload, NetworkCompatibilityWireCodec.MaximumEncodedBytes);
        if (serializer.IsReading) {
            Manifest = NetworkCompatibilityWireCodec.DecodeManifest(payload);
        }
    }

    public override void Handle(bool isServer) {
        if (!isServer
            || From == null
            || From.CompatibilityState != NetworkCompatibilityState.AwaitingHello) {
            return;
        }
        NetworkCompatibilityResult result = NetworkCompatibility.Evaluate(
            NetworkCompatibilityRuntime.GetLocalManifest(),
            Manifest);
        From.CompatibilityResult = result;
        From.CompatibilityState = result.Accepted
            ? NetworkCompatibilityState.AwaitingChannels
            : NetworkCompatibilityState.Rejected;
        NetworkManager.Queue(new CompatibilityResultPacket(result) { To = From });
        if (result.Accepted) {
            Log.Information("Network compatibility handshake accepted.");
        }
        else {
            NetworkSessionLifecycleTracker.RecordCompatibilityRejected(
                From,
                result.PrimaryCode.ToString());
            Log.Warning($"Network compatibility handshake rejected: {result.Summary}");
        }
    }
}

public sealed class CompatibilityResultPacket : Packet {
    public override byte ID => (byte)PacketType.CompatibilityResult;

    public NetworkCompatibilityResult Result = new();

    public CompatibilityResultPacket() {
    }

    public CompatibilityResultPacket(NetworkCompatibilityResult result) {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public override void Serialize(PacketSerializer serializer) {
        byte[] payload = serializer.IsWriting
            ? NetworkCompatibilityWireCodec.EncodeResult(Result)
            : null;
        serializer.Buffer(ref payload, NetworkCompatibilityWireCodec.MaximumEncodedBytes);
        if (serializer.IsReading) {
            Result = NetworkCompatibilityWireCodec.DecodeResult(payload);
        }
    }

    public override void Handle(bool isServer) {
        ClientSession session = NetworkManager.MySession;
        if (isServer
            || session == null
            || session.CompatibilityState != NetworkCompatibilityState.AwaitingHello) {
            return;
        }
        NetworkCompatibilityRuntime.RecordClientCoreResult(session, Result);
        if (Result.Accepted) {
            Log.Information("Server accepted API/mod compatibility; negotiating mod channels.");
            ModNetworkChannelCatalog catalog = ModNetworkManager.GetLocalCatalog();
            session.ModChannelCatalogFingerprint =
                ModNetworkProtocol.ComputeCatalogFingerprint(catalog);
            NetworkManager.Queue(new ModChannelCatalogPacket(catalog));
        }
        else {
            Log.Warning($"Server rejected network compatibility handshake: {Result.Summary}");
        }
    }
}
