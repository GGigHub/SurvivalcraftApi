using System;
using System.Collections.Generic;
using System.Linq;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public sealed class ModNetworkMessageContext {
    internal ModNetworkMessageContext(
        bool isServer,
        ClientSession sender,
        Project project,
        ModNetworkChannelDescriptor channel,
        Guid messageId) {
        IsServer = isServer;
        Sender = sender;
        Project = project;
        Channel = channel.Clone();
        MessageId = messageId;
    }

    public bool IsServer { get; }
    public ClientSession Sender { get; }
    public Project Project { get; }
    public ModNetworkChannelDescriptor Channel { get; }
    public Guid MessageId { get; }
}

public readonly record struct ModNetworkRuntimeMetrics(
    long Sent,
    long Received,
    long Delivered,
    long Dropped,
    long Duplicates,
    long RateLimited,
    long HandlerErrors,
    int ActivePeerStates);

public sealed record ModNetworkDispatchRecord(
    DateTimeOffset Timestamp,
    string PackageName,
    string ChannelName,
    Guid MessageId,
    ModNetworkDispatchCode Code,
    bool IsServer,
    int PayloadBytes,
    string Detail);

public sealed class ModNetworkChannel : IDisposable {
    internal ModNetworkChannel(
        Guid registrationId,
        ModLoader owner,
        ModNetworkChannelDescriptor descriptor) {
        RegistrationId = registrationId;
        Owner = owner;
        Descriptor = descriptor.Clone();
    }

    internal Guid RegistrationId { get; }
    internal ModLoader Owner { get; }

    public ModNetworkChannelDescriptor Descriptor { get; }
    public bool IsRegistered => ModNetworkManager.IsRegistered(this);

    public void SendToServer(byte[] payload) =>
        ModNetworkManager.SendToServer(this, payload);

    public void SendToClient(ClientSession session, byte[] payload) =>
        ModNetworkManager.SendToClient(this, session, payload);

    public int Broadcast(byte[] payload, ClientSession except = null) =>
        ModNetworkManager.Broadcast(this, payload, except);

    public void Dispose() => ModNetworkManager.Unregister(this);
}

public static class ModNetworkManager {
    private sealed class Registration {
        public Guid Id;
        public ModLoader Owner;
        public ModNetworkChannelDescriptor Descriptor;
        public Action<ModNetworkMessageContext, ReadOnlyMemory<byte>> Handler;
        public ModNetworkChannel Handle;
    }

    private sealed class PeerState {
        public ModNetworkTrafficLimiter TrafficLimiter = new();
        public ModNetworkMessageDeduplicator Deduplicator = new();
    }

    private static readonly object s_sync = new();
    private static readonly Dictionary<string, Registration> s_registrations = new(StringComparer.Ordinal);
    private static readonly Dictionary<ModLoader, HashSet<string>> s_ownerKeys = [];
    private static readonly Dictionary<ClientSession, PeerState> s_peerStates = [];

    private static ModNetworkChannelCatalog s_catalogOverride;
    private static long s_sent;
    private static long s_received;
    private static long s_delivered;
    private static long s_dropped;
    private static long s_duplicates;
    private static long s_rateLimited;
    private static long s_handlerErrors;
    private static ModNetworkDispatchRecord s_lastDispatch;

    public static ModNetworkRuntimeMetrics Metrics {
        get {
            lock (s_sync) {
                return new ModNetworkRuntimeMetrics(
                    s_sent,
                    s_received,
                    s_delivered,
                    s_dropped,
                    s_duplicates,
                    s_rateLimited,
                    s_handlerErrors,
                    s_peerStates.Count);
            }
        }
    }

    public static ModNetworkDispatchRecord LastDispatch {
        get {
            lock (s_sync) {
                return s_lastDispatch;
            }
        }
    }

    public static bool HasCatalogOverride {
        get {
            lock (s_sync) {
                return s_catalogOverride != null;
            }
        }
    }

    public static ModNetworkChannel RegisterChannel(
        ModLoader owner,
        string channelName,
        int version,
        ModNetworkDirection directions,
        int maximumPayloadBytes,
        Action<ModNetworkMessageContext, ReadOnlyMemory<byte>> handler) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);
        EnsureCatalogCanChange();
        string packageName = owner.Entity?.modInfo?.PackageName;
        if (string.IsNullOrWhiteSpace(packageName)) {
            throw new InvalidOperationException(
                "A mod network channel can only be registered by an initialized ModLoader with a package identity.");
        }
        if (owner.Entity.modInfo.NetworkScope == ModNetworkScope.LocalOnly) {
            throw new InvalidOperationException(
                $"Local-only mod '{packageName}' cannot register multiplayer network channels.");
        }

        ModNetworkChannelDescriptor requested = new(
            packageName,
            channelName,
            version,
            directions,
            maximumPayloadBytes);
        if (!ModNetworkProtocol.TryCanonicalizeDescriptor(
                requested,
                out ModNetworkChannelDescriptor descriptor,
                out string error)) {
            throw new ArgumentException(error, nameof(channelName));
        }

        string key = ModNetworkProtocol.BuildKey(descriptor);
        lock (s_sync) {
            if (s_registrations.ContainsKey(key)) {
                throw new InvalidOperationException(
                    $"Mod network channel '{descriptor.QualifiedName}' is already registered.");
            }
            if (s_registrations.Count >= ModNetworkProtocol.MaximumChannelCount) {
                throw new InvalidOperationException(
                    $"No more than {ModNetworkProtocol.MaximumChannelCount} mod network channels can be registered.");
            }
            int packageChannelCount = s_registrations.Values.Count(registration =>
                string.Equals(
                    registration.Descriptor.PackageName,
                    descriptor.PackageName,
                    StringComparison.Ordinal));
            if (packageChannelCount >= ModNetworkProtocol.MaximumChannelsPerPackage) {
                throw new InvalidOperationException(
                    $"Mod '{descriptor.PackageName}' cannot register more than {ModNetworkProtocol.MaximumChannelsPerPackage} channels.");
            }
            s_ownerKeys.TryGetValue(owner, out HashSet<string> existingKeys);

            Guid registrationId = Guid.NewGuid();
            ModNetworkChannel handle = new(registrationId, owner, descriptor);
            Registration registration = new() {
                Id = registrationId,
                Owner = owner,
                Descriptor = descriptor,
                Handler = handler,
                Handle = handle
            };
            s_registrations.Add(key, registration);
            if (existingKeys == null) {
                existingKeys = [];
                s_ownerKeys.Add(owner, existingKeys);
            }
            existingKeys.Add(key);
            return handle;
        }
    }

    public static ModNetworkChannelCatalog GetLocalCatalog() {
        lock (s_sync) {
            ModNetworkChannelCatalog source = s_catalogOverride
                ?? new ModNetworkChannelCatalog(
                    s_registrations.Values.Select(registration => registration.Descriptor));
            if (!ModNetworkProtocol.TryCanonicalizeCatalog(
                    source,
                    out ModNetworkChannelCatalog canonical,
                    out string error)) {
                throw new InvalidOperationException($"Registered mod channel catalog is invalid: {error}");
            }
            return canonical;
        }
    }

    internal static void SetNegotiatedCatalog(
        ClientSession session,
        ModNetworkChannelCatalog catalog) {
        ArgumentNullException.ThrowIfNull(session);
        if (!ModNetworkProtocol.TryCanonicalizeCatalog(
                catalog,
                out ModNetworkChannelCatalog canonical,
                out string error)) {
            throw new InvalidOperationException(
                $"Negotiated mod channel catalog is invalid: {error}");
        }
        lock (s_sync) {
            session.NegotiatedModChannelKeys.Clear();
            foreach (ModNetworkChannelDescriptor channel in canonical.Channels) {
                session.NegotiatedModChannelKeys.Add(ModNetworkProtocol.BuildKey(channel));
            }
            session.ModChannelCatalogFingerprint =
                ModNetworkProtocol.ComputeCatalogFingerprint(canonical);
        }
    }

    internal static void ClearNegotiatedCatalog(ClientSession session) {
        if (session == null) {
            return;
        }
        lock (s_sync) {
            session.NegotiatedModChannelKeys.Clear();
            session.ModChannelCatalogFingerprint = string.Empty;
        }
    }

    public static bool IsChannelNegotiated(
        ClientSession session,
        ModNetworkChannelDescriptor descriptor) {
        if (session == null || descriptor == null) {
            return false;
        }
        string key = ModNetworkProtocol.BuildKey(descriptor);
        lock (s_sync) {
            return session.NegotiatedModChannelKeys.Contains(key);
        }
    }

    public static void SetLocalCatalogOverride(ModNetworkChannelCatalog catalog) {
        EnsureCatalogCanChange();
        lock (s_sync) {
            s_catalogOverride = catalog?.Clone();
        }
    }

    public static void ClearLocalCatalogOverride() {
        EnsureCatalogCanChange();
        lock (s_sync) {
            s_catalogOverride = null;
        }
    }

    internal static bool IsRegistered(ModNetworkChannel channel) {
        if (channel == null) {
            return false;
        }
        string key = ModNetworkProtocol.BuildKey(channel.Descriptor);
        lock (s_sync) {
            return s_registrations.TryGetValue(key, out Registration registration)
                && registration.Id == channel.RegistrationId;
        }
    }

    internal static void Unregister(ModNetworkChannel channel) {
        if (channel == null) {
            return;
        }
        string key = ModNetworkProtocol.BuildKey(channel.Descriptor);
        lock (s_sync) {
            if (!s_registrations.TryGetValue(key, out Registration registration)
                || registration.Id != channel.RegistrationId) {
                return;
            }
            s_registrations.Remove(key);
            if (s_ownerKeys.TryGetValue(registration.Owner, out HashSet<string> keys)) {
                keys.Remove(key);
                if (keys.Count == 0) {
                    s_ownerKeys.Remove(registration.Owner);
                }
            }
        }
    }

    internal static void UnregisterOwner(ModLoader owner) {
        if (owner == null) {
            return;
        }
        lock (s_sync) {
            if (!s_ownerKeys.Remove(owner, out HashSet<string> keys)) {
                return;
            }
            foreach (string key in keys) {
                s_registrations.Remove(key);
            }
        }
    }

    public static void SendToServer(ModNetworkChannel channel, byte[] payload) {
        Registration registration = GetRegistration(channel);
        EnsureDirection(registration, ModNetworkDirection.ClientToServer);
        ValidatePayload(registration.Descriptor, payload);
        ClientSession session = NetworkManager.MySession;
        if (!NetworkManager.IsClientRunning
            || session == null
            || session.CompatibilityState != NetworkCompatibilityState.Accepted
            || session.NetworkState < NetworkState.ProjectLoaded) {
            throw new InvalidOperationException(
                $"Mod channel '{registration.Descriptor.QualifiedName}' cannot send before the client project is loaded.");
        }
        if (!IsChannelNegotiated(session, registration.Descriptor)) {
            throw new InvalidOperationException(
                $"Mod channel '{registration.Descriptor.QualifiedName}' was not negotiated for this server session.");
        }

        ModNetworkEnvelope envelope = CreateEnvelope(registration.Descriptor, payload);
        NetworkManager.SendModMessageToServer(envelope);
        RecordSent(registration.Descriptor, envelope, isServer: false);
    }

    public static void SendToClient(
        ModNetworkChannel channel,
        ClientSession session,
        byte[] payload) {
        ArgumentNullException.ThrowIfNull(session);
        Registration registration = GetRegistration(channel);
        EnsureDirection(registration, ModNetworkDirection.ServerToClient);
        ValidatePayload(registration.Descriptor, payload);
        if (!NetworkManager.IsServerRunning
            || session.CompatibilityState != NetworkCompatibilityState.Accepted
            || session.NetworkState < NetworkState.ProjectLoaded
            || !NetworkManager.ServerSessions.Contains(session)) {
            throw new InvalidOperationException(
                $"Mod channel '{registration.Descriptor.QualifiedName}' cannot send to an inactive client session.");
        }
        if (!IsChannelNegotiated(session, registration.Descriptor)) {
            throw new InvalidOperationException(
                $"Mod channel '{registration.Descriptor.QualifiedName}' was not negotiated for this client session.");
        }

        ModNetworkEnvelope envelope = CreateEnvelope(registration.Descriptor, payload);
        NetworkManager.SendModMessageToClient(envelope, session);
        RecordSent(registration.Descriptor, envelope, isServer: true);
    }

    public static int Broadcast(
        ModNetworkChannel channel,
        byte[] payload,
        ClientSession except = null) {
        Registration registration = GetRegistration(channel);
        EnsureDirection(registration, ModNetworkDirection.ServerToClient);
        ValidatePayload(registration.Descriptor, payload);
        ClientSession[] recipients = NetworkManager.ServerSessions
            .Where(session => session != except
                && session.CompatibilityState == NetworkCompatibilityState.Accepted
                && session.NetworkState >= NetworkState.ProjectLoaded
                && IsChannelNegotiated(session, registration.Descriptor))
            .Distinct()
            .ToArray();
        if (recipients.Length == 0) {
            return 0;
        }

        ModNetworkEnvelope envelope = CreateEnvelope(registration.Descriptor, payload);
        foreach (ClientSession recipient in recipients) {
            NetworkManager.SendModMessageToClient(envelope, recipient);
        }
        RecordSent(registration.Descriptor, envelope, isServer: true);
        return recipients.Length;
    }

    public static ModNetworkDispatchCode Dispatch(
        ModNetworkEnvelope envelope,
        bool isServer,
        ClientSession sender) {
        ArgumentNullException.ThrowIfNull(envelope);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        Registration registration;
        ClientSession peer = isServer ? sender : NetworkManager.MySession;
        lock (s_sync) {
            s_received++;
            if (peer == null) {
                return RecordDispatchLocked(
                    envelope,
                    ModNetworkDispatchCode.DirectionDenied,
                    isServer,
                    "No authenticated peer session is associated with the message.",
                    timestamp);
            }
            PeerState peerState = GetOrCreatePeerStateLocked(peer);
            if (!peerState.TrafficLimiter.TryConsume(
                    envelope.Payload?.Length ?? 0,
                    timestamp.ToUnixTimeMilliseconds())) {
                s_rateLimited++;
                return RecordDispatchLocked(
                    envelope,
                    ModNetworkDispatchCode.RateLimited,
                    isServer,
                    "Peer exceeded the mod channel traffic window.",
                    timestamp);
            }
            if (!peerState.Deduplicator.TryRemember(envelope.MessageId)) {
                s_duplicates++;
                return RecordDispatchLocked(
                    envelope,
                    ModNetworkDispatchCode.Duplicate,
                    isServer,
                    "Duplicate message identity was ignored.",
                    timestamp);
            }
            if (!peer.NegotiatedModChannelKeys.Contains(
                    ModNetworkProtocol.BuildKey(
                        envelope.PackageName,
                        envelope.ChannelName))) {
                return RecordDispatchLocked(
                    envelope,
                    ModNetworkDispatchCode.UnnegotiatedChannel,
                    isServer,
                    "Channel was not negotiated for this peer session.",
                    timestamp);
            }
            if (!s_registrations.TryGetValue(
                    ModNetworkProtocol.BuildKey(envelope.PackageName, envelope.ChannelName),
                    out registration)) {
                return RecordDispatchLocked(
                    envelope,
                    ModNetworkDispatchCode.UnknownChannel,
                    isServer,
                    "No local handler is registered for the channel.",
                    timestamp);
            }
        }

        ModNetworkChannelDescriptor descriptor = registration.Descriptor;
        if (envelope.ChannelVersion != descriptor.Version) {
            return RecordDispatch(
                envelope,
                ModNetworkDispatchCode.VersionMismatch,
                isServer,
                $"Expected channel version {descriptor.Version}.",
                timestamp);
        }
        ModNetworkDirection requiredDirection = isServer
            ? ModNetworkDirection.ClientToServer
            : ModNetworkDirection.ServerToClient;
        if ((descriptor.Directions & requiredDirection) == 0) {
            return RecordDispatch(
                envelope,
                ModNetworkDispatchCode.DirectionDenied,
                isServer,
                $"Channel does not allow {requiredDirection} traffic.",
                timestamp);
        }
        if ((envelope.Payload?.Length ?? 0) > descriptor.MaximumPayloadBytes) {
            return RecordDispatch(
                envelope,
                ModNetworkDispatchCode.PayloadTooLarge,
                isServer,
                $"Channel payload limit is {descriptor.MaximumPayloadBytes} bytes.",
                timestamp);
        }
        Project project = GameManager.Project;
        if (project == null) {
            return RecordDispatch(
                envelope,
                ModNetworkDispatchCode.ProjectUnavailable,
                isServer,
                "No project is loaded.",
                timestamp);
        }

        ModNetworkMessageContext context = new(
            isServer,
            sender,
            project,
            descriptor,
            envelope.MessageId);
        try {
            registration.Handler(context, envelope.Payload ?? []);
            return RecordDispatch(
                envelope,
                ModNetworkDispatchCode.Delivered,
                isServer,
                string.Empty,
                timestamp);
        }
        catch (Exception exception) {
            Log.Warning(
                $"Mod network handler '{descriptor.QualifiedName}' failed and was isolated: {exception}");
            lock (s_sync) {
                s_handlerErrors++;
            }
            return RecordDispatch(
                envelope,
                ModNetworkDispatchCode.HandlerFailed,
                isServer,
                exception.Message,
                timestamp);
        }
    }

    public static void ClearSession(ClientSession session) {
        if (session == null) {
            return;
        }
        lock (s_sync) {
            s_peerStates.Remove(session);
            session.NegotiatedModChannelKeys.Clear();
            session.ModChannelCatalogFingerprint = string.Empty;
        }
    }

    public static void OnProjectDisposed() {
        lock (s_sync) {
            s_peerStates.Clear();
        }
    }

    public static void ResetRegistrations() {
        lock (s_sync) {
            s_registrations.Clear();
            s_ownerKeys.Clear();
            s_catalogOverride = null;
            s_peerStates.Clear();
            ResetMetricsLocked();
        }
    }

    public static void ResetMetrics() {
        lock (s_sync) {
            ResetMetricsLocked();
        }
    }

    private static Registration GetRegistration(ModNetworkChannel channel) {
        ArgumentNullException.ThrowIfNull(channel);
        string key = ModNetworkProtocol.BuildKey(channel.Descriptor);
        lock (s_sync) {
            if (!s_registrations.TryGetValue(key, out Registration registration)
                || registration.Id != channel.RegistrationId) {
                throw new ObjectDisposedException(
                    nameof(ModNetworkChannel),
                    $"Mod network channel '{channel.Descriptor.QualifiedName}' is not registered.");
            }
            return registration;
        }
    }

    private static void EnsureCatalogCanChange() {
        if (NetworkManager.IsServerRunning || NetworkManager.IsClientRunning) {
            throw new InvalidOperationException(
                "Mod network channels must be declared before starting a multiplayer transport.");
        }
    }

    private static void EnsureDirection(
        Registration registration,
        ModNetworkDirection direction) {
        if ((registration.Descriptor.Directions & direction) == 0) {
            throw new InvalidOperationException(
                $"Mod network channel '{registration.Descriptor.QualifiedName}' does not allow {direction} traffic.");
        }
    }

    private static void ValidatePayload(
        ModNetworkChannelDescriptor descriptor,
        byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > descriptor.MaximumPayloadBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"Mod network channel '{descriptor.QualifiedName}' allows at most {descriptor.MaximumPayloadBytes} bytes.");
        }
    }

    private static ModNetworkEnvelope CreateEnvelope(
        ModNetworkChannelDescriptor descriptor,
        byte[] payload) =>
        new(
            Guid.NewGuid(),
            descriptor.PackageName,
            descriptor.ChannelName,
            descriptor.Version,
            payload);

    private static void RecordSent(
        ModNetworkChannelDescriptor descriptor,
        ModNetworkEnvelope envelope,
        bool isServer) {
        lock (s_sync) {
            s_sent++;
            s_lastDispatch = new ModNetworkDispatchRecord(
                DateTimeOffset.UtcNow,
                descriptor.PackageName,
                descriptor.ChannelName,
                envelope.MessageId,
                ModNetworkDispatchCode.Delivered,
                isServer,
                envelope.Payload?.Length ?? 0,
                "sent");
        }
    }

    private static ModNetworkDispatchCode RecordDispatch(
        ModNetworkEnvelope envelope,
        ModNetworkDispatchCode code,
        bool isServer,
        string detail,
        DateTimeOffset timestamp) {
        lock (s_sync) {
            return RecordDispatchLocked(envelope, code, isServer, detail, timestamp);
        }
    }

    private static ModNetworkDispatchCode RecordDispatchLocked(
        ModNetworkEnvelope envelope,
        ModNetworkDispatchCode code,
        bool isServer,
        string detail,
        DateTimeOffset timestamp) {
        if (code == ModNetworkDispatchCode.Delivered) {
            s_delivered++;
        }
        else {
            s_dropped++;
        }
        s_lastDispatch = new ModNetworkDispatchRecord(
            timestamp,
            envelope.PackageName ?? string.Empty,
            envelope.ChannelName ?? string.Empty,
            envelope.MessageId,
            code,
            isServer,
            envelope.Payload?.Length ?? 0,
            detail ?? string.Empty);
        return code;
    }

    private static PeerState GetOrCreatePeerStateLocked(ClientSession session) {
        if (!s_peerStates.TryGetValue(session, out PeerState state)) {
            state = new PeerState();
            s_peerStates.Add(session, state);
        }
        return state;
    }

    private static void ResetMetricsLocked() {
        s_sent = 0;
        s_received = 0;
        s_delivered = 0;
        s_dropped = 0;
        s_duplicates = 0;
        s_rateLimited = 0;
        s_handlerErrors = 0;
        s_lastDispatch = null;
    }
}
