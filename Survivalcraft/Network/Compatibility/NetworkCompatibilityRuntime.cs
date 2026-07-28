using System;
using System.Collections.Generic;

namespace Game.Network;

public static class NetworkCompatibilityRuntime {
    public static readonly TimeSpan ClientHandshakeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ServerHandshakeTimeout = TimeSpan.FromSeconds(20);

    private static readonly HashSet<string> s_builtinPackages = new(StringComparer.OrdinalIgnoreCase) {
        "survivalcraft",
        "fastdebug",
        "debug",
        "network"
    };

    private static NetworkCompatibilityManifest s_localManifestOverride;
    private static NetworkCompatibilityResult s_pendingClientRejection;

    public static NetworkCompatibilityResult LastClientResult { get; private set; }
    public static NetworkCompatibilityResult LastClientCoreResult { get; private set; }
    public static NetworkCompatibilityManifest LastClientHello { get; private set; }
    internal static bool HasLocalManifestOverride => s_localManifestOverride != null;

    public static NetworkCompatibilityManifest CreateLoadedManifest() {
        List<NetworkModIdentity> mods = [];
        foreach (ModEntity modEntity in ModsManager.ModList) {
            ModInfo info = modEntity?.modInfo;
            if (info == null
                || modEntity is SurvivalCraftModEntity
                || modEntity is FastDebugModEntity
                || modEntity is NetworkModEntity
                || s_builtinPackages.Contains(info.PackageName ?? string.Empty)) {
                continue;
            }
            mods.Add(new NetworkModIdentity(
                info.PackageName,
                info.Version,
                info.ApiVersion));
        }

        NetworkCompatibilityManifest manifest = new(
            NetworkCompatibility.ProtocolRevision,
            ModsManager.APIVersionString,
            NetworkCompatibility.CurrentCapabilities,
            mods);
        if (!NetworkCompatibility.TryCanonicalize(
                manifest,
                out NetworkCompatibilityManifest canonical,
                out string error)) {
            throw new InvalidOperationException($"Loaded mod manifest is invalid: {error}");
        }
        return canonical;
    }

    public static NetworkCompatibilityManifest GetLocalManifest() =>
        (s_localManifestOverride ?? CreateLoadedManifest()).Clone();

    internal static void SetLocalManifestOverride(NetworkCompatibilityManifest manifest) {
        s_localManifestOverride = manifest?.Clone();
    }

    internal static void ClearLocalManifestOverride() {
        s_localManifestOverride = null;
    }

    public static NetworkCompatibilityManifest BeginClientHandshake(ClientSession session) {
        ArgumentNullException.ThrowIfNull(session);
        NetworkCompatibilityManifest manifest = GetLocalManifest();
        LastClientHello = manifest.Clone();
        LastClientResult = null;
        LastClientCoreResult = null;
        s_pendingClientRejection = null;
        session.CompatibilityState = NetworkCompatibilityState.AwaitingHello;
        session.CompatibilityResult = null;
        session.ChannelCompatibilityResult = null;
        session.ModChannelCatalogFingerprint = string.Empty;
        session.CompatibilityStartedUtc = DateTime.UtcNow;
        return manifest;
    }

    public static void UpdateClientHandshake(ClientSession session) {
        if (session == null
            || (session.CompatibilityState != NetworkCompatibilityState.AwaitingHello
                && session.CompatibilityState != NetworkCompatibilityState.AwaitingChannels)
            || session.CompatibilityStartedUtc == default
            || DateTime.UtcNow - session.CompatibilityStartedUtc < ClientHandshakeTimeout) {
            return;
        }

        RecordClientFinalResult(
            session,
            NetworkCompatibility.CreateHandshakeTimeoutResult(
                (int)ClientHandshakeTimeout.TotalSeconds,
                session.CompatibilityState));
    }

    public static void RecordClientCoreResult(
        ClientSession session,
        NetworkCompatibilityResult result) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        session.CompatibilityResult = result;
        LastClientCoreResult = result;
        if (result.Accepted) {
            session.CompatibilityState = NetworkCompatibilityState.AwaitingChannels;
            return;
        }
        RecordClientFinalResult(session, result);
    }

    public static void RecordClientChannelResult(
        ClientSession session,
        NetworkCompatibilityResult result) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        session.ChannelCompatibilityResult = result;
        RecordClientFinalResult(session, result);
    }

    public static void RecordClientFinalResult(
        ClientSession session,
        NetworkCompatibilityResult result) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        session.CompatibilityResult = result;
        session.CompatibilityState = result.Accepted
            ? NetworkCompatibilityState.Accepted
            : NetworkCompatibilityState.Rejected;
        LastClientResult = result;
        if (!result.Accepted) {
            s_pendingClientRejection = result;
        }
    }

    public static bool TryTakePendingClientRejection(out NetworkCompatibilityResult result) {
        result = s_pendingClientRejection;
        s_pendingClientRejection = null;
        return result != null;
    }

    public static void RejectPrematurePacket(ClientSession session, Packet packet) {
        if (session == null) {
            return;
        }
        NetworkCompatibilityResult result = NetworkCompatibility.CreateHandshakeRequiredResult(
            packet?.GetType().Name ?? "unknown packet");
        bool awaitingChannels = session.CompatibilityState == NetworkCompatibilityState.AwaitingChannels;
        session.CompatibilityState = NetworkCompatibilityState.Rejected;
        session.CompatibilityResult = result;
        if (awaitingChannels) {
            NetworkManager.Queue(new ModChannelCatalogResultPacket(result) { To = session });
        }
        else {
            NetworkManager.Queue(new CompatibilityResultPacket(result) { To = session });
        }
    }
}
