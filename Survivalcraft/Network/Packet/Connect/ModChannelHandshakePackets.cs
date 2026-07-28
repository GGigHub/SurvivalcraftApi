using Engine;

namespace Game.Network;

public sealed class ModChannelCatalogPacket : Packet {
    public override byte ID => (byte)PacketType.ModChannelCatalog;

    public ModNetworkChannelCatalog Catalog = new();

    public ModChannelCatalogPacket() {
    }

    public ModChannelCatalogPacket(ModNetworkChannelCatalog catalog) {
        Catalog = catalog?.Clone() ?? new ModNetworkChannelCatalog();
    }

    public override void Serialize(PacketSerializer serializer) {
        byte[] payload = serializer.IsWriting
            ? ModNetworkProtocol.EncodeCatalog(Catalog)
            : null;
        serializer.Buffer(ref payload, ModNetworkProtocol.MaximumCatalogEncodedBytes);
        if (serializer.IsReading) {
            Catalog = ModNetworkProtocol.DecodeCatalog(payload);
        }
    }

    public override void Handle(bool isServer) {
        if (!isServer
            || From == null
            || From.CompatibilityState != NetworkCompatibilityState.AwaitingChannels) {
            return;
        }
        NetworkCompatibilityResult result = ModNetworkProtocol.EvaluateCatalogs(
            ModNetworkManager.GetLocalCatalog(),
            Catalog);
        From.ChannelCompatibilityResult = result;
        From.ModChannelCatalogFingerprint = ModNetworkProtocol.ComputeCatalogFingerprint(Catalog);
        From.CompatibilityResult = result;
        From.CompatibilityState = result.Accepted
            ? NetworkCompatibilityState.Accepted
            : NetworkCompatibilityState.Rejected;
        NetworkManager.Queue(new ModChannelCatalogResultPacket(result) { To = From });
        if (result.Accepted) {
            Log.Information("Mod channel catalog accepted.");
        }
        else {
            Log.Warning($"Mod channel catalog rejected: {result.Summary}");
        }
    }
}

public sealed class ModChannelCatalogResultPacket : Packet {
    public override byte ID => (byte)PacketType.ModChannelCatalogResult;

    public NetworkCompatibilityResult Result = new();

    public ModChannelCatalogResultPacket() {
    }

    public ModChannelCatalogResultPacket(NetworkCompatibilityResult result) {
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
            || session.CompatibilityState != NetworkCompatibilityState.AwaitingChannels) {
            return;
        }
        NetworkCompatibilityRuntime.RecordClientChannelResult(session, Result);
        if (Result.Accepted) {
            Log.Information("Server accepted mod channel catalog.");
            NetworkManager.Queue(new ConnectPacket(NetworkManager.NetworkGuid));
        }
        else {
            Log.Warning($"Server rejected mod channel catalog: {Result.Summary}");
        }
    }
}
