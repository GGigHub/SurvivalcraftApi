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
        ModNetworkCatalogNegotiation negotiation = ModNetworkProtocol.NegotiateCatalogs(
            ModNetworkManager.GetLocalCatalog(),
            Catalog,
            From.LocalCompatibilityManifest,
            From.RemoteCompatibilityManifest);
        NetworkCompatibilityResult result = negotiation.Result;
        From.ChannelCompatibilityResult = result;
        From.CompatibilityResult = result;
        From.CompatibilityState = result.Accepted
            ? NetworkCompatibilityState.Accepted
            : NetworkCompatibilityState.Rejected;
        if (result.Accepted) {
            ModNetworkManager.SetNegotiatedCatalog(From, negotiation.Catalog);
        }
        else {
            ModNetworkManager.ClearNegotiatedCatalog(From);
        }
        NetworkManager.Queue(new ModChannelCatalogResultPacket(
            result,
            negotiation.Catalog) { To = From });
        if (result.Accepted) {
            NetworkSessionAdmissionRuntime.BeginServerAdmission(From);
            NetworkSessionLifecycleTracker.RecordCompatibilityAccepted(From);
            Log.Information("Mod channel catalog accepted.");
        }
        else {
            NetworkSessionLifecycleTracker.RecordCompatibilityRejected(
                From,
                result.PrimaryCode.ToString());
            Log.Warning($"Mod channel catalog rejected: {result.Summary}");
        }
    }
}

public sealed class ModChannelCatalogResultPacket : Packet {
    public override byte ID => (byte)PacketType.ModChannelCatalogResult;

    public NetworkCompatibilityResult Result = new();
    public ModNetworkChannelCatalog Catalog = new();

    public ModChannelCatalogResultPacket() {
    }

    public ModChannelCatalogResultPacket(
        NetworkCompatibilityResult result,
        ModNetworkChannelCatalog catalog = null) {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Catalog = catalog?.Clone() ?? new ModNetworkChannelCatalog();
    }

    public override void Serialize(PacketSerializer serializer) {
        byte[] payload = serializer.IsWriting
            ? NetworkCompatibilityWireCodec.EncodeResult(Result)
            : null;
        serializer.Buffer(ref payload, NetworkCompatibilityWireCodec.MaximumEncodedBytes);
        if (serializer.IsReading) {
            Result = NetworkCompatibilityWireCodec.DecodeResult(payload);
        }
        byte[] catalogPayload = serializer.IsWriting
            ? ModNetworkProtocol.EncodeCatalog(Catalog)
            : null;
        serializer.Buffer(ref catalogPayload, ModNetworkProtocol.MaximumCatalogEncodedBytes);
        if (serializer.IsReading) {
            Catalog = ModNetworkProtocol.DecodeCatalog(catalogPayload);
        }
    }

    public override void Handle(bool isServer) {
        ClientSession session = NetworkManager.MySession;
        if (isServer
            || session == null
            || session.CompatibilityState != NetworkCompatibilityState.AwaitingChannels) {
            return;
        }
        if (Result.Accepted) {
            if (!ModNetworkProtocol.TryValidateNegotiatedCatalog(
                    ModNetworkManager.GetLocalCatalog(),
                    Catalog,
                    out ModNetworkChannelCatalog negotiated,
                    out string error)) {
                NetworkCompatibilityResult invalidCatalog =
                    NetworkCompatibilityResult.Reject([
                        new NetworkCompatibilityIssue(
                            NetworkCompatibilityIssueCode.InvalidServerChannelCatalog,
                            string.Empty,
                            "a subset of the local channel catalog",
                            error,
                            $"Server returned an invalid negotiated channel catalog: {error}")
                    ]);
                ModNetworkManager.ClearNegotiatedCatalog(session);
                NetworkCompatibilityRuntime.RecordClientChannelResult(
                    session,
                    invalidCatalog);
                Log.Warning(invalidCatalog.Summary);
                return;
            }
            ModNetworkManager.SetNegotiatedCatalog(session, negotiated);
            NetworkCompatibilityRuntime.RecordClientChannelResult(session, Result);
            Log.Information("Server accepted mod channel catalog.");
            NetworkSessionAdmissionRuntime.BeginClientAdmission(session);
            NetworkManager.Queue(new ConnectPacket(NetworkManager.NetworkGuid));
        }
        else {
            ModNetworkManager.ClearNegotiatedCatalog(session);
            NetworkCompatibilityRuntime.RecordClientChannelResult(session, Result);
            Log.Warning($"Server rejected mod channel catalog: {Result.Summary}");
        }
    }
}
