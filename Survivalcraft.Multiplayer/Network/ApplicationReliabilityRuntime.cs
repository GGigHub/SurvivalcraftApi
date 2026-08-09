using System.Threading;
using Engine;

namespace Game.Network;

public readonly record struct ApplicationReliabilityMetrics(
    long Created,
    long Retried,
    long Acknowledged,
    long DuplicateDeliveries,
    long TimedOut,
    long Failed,
    int Pending,
    long PendingBytes);

internal static class ApplicationReliabilityPolicy {
    internal static bool ShouldProtect(Packet packet, ClientSession session) {
        ArgumentNullException.ThrowIfNull(packet);
        if (session?.AdmissionState != NetworkSessionAdmissionState.Accepted) {
            return false;
        }

        // Compatibility/admission already have their own bounded handshake state
        // machines. The remaining exclusions are high-rate, replaceable streams;
        // their newer revision/full snapshot heals a dropped older sample.
        return packet is not ApplicationReliablePacket
            and not CompatibilityHelloPacket
            and not CompatibilityResultPacket
            and not ModChannelCatalogPacket
            and not ModChannelCatalogResultPacket
            and not ConnectPacket
            and not SessionAdmissionResultPacket
            and not LatencyProbePacket
            and not EntitySyncRequestPacket
            and not PlayerBodyUpdatePacket
            and not PlayerAimPacket
            and not BehaviorSyncPacket
            and not LookAroundBehaviorPacket
            and not StubbornSteedBehaviorPacket
            and not ChaseBehaviorPacket
            and not RandomPeckBehaviorPacket
            and not RandomFeedBehaviorPacket
            and not EatPickableBehaviorPacket
            and not HumanRowBehaviorPacket
            and not DigInMudBehaviorPacket
            and not FishOutOfWaterBehaviorPacket
            and not ComponentFluPacket
            and not ComponentSicknessPacket
            and not VitalStatSyncPacket
            and not PlayerStatSyncPacket
            and not EntityOnFirePacket
            and not PickableUpdatePacket
            and not SubsystemElectricityPacket
            and not SubsystemTimePacket
            and not SubsystemSkyPacket
            and not SubsystemWeatherPacket
            and not TerrainViewDistancePacket
            // The normal dig frame deliberately remains replaceable because the
            // correlated PlayerDigRequest/Confirmation recovery path owns retry.
            and not PlayerDigPacket;
    }

    internal static bool ShouldReplayDuplicateForDomainEvidence(Packet packet) =>
        // These packets carry stable domain identities and bounded
        // deduplicators. Let them observe repeated reliable frames so their
        // public duplicate metrics remain truthful without repeating effects.
        packet is ModMessagePacket or ProjectilePacket or ExplosionPacket;
}

internal sealed class ApplicationReliabilityReceiveWindow {
    internal const int MaximumBufferedDeliveries = 4096;
    internal const long MaximumBufferedBytes = 64L * 1024 * 1024;
    internal const long MaximumSequenceGap = 16 * 1024;

    private readonly SortedDictionary<long, BufferedDelivery> buffered = [];
    private long nextDeliveryId = 1;

    internal int Count => buffered.Count;
    internal long Bytes { get; private set; }
    internal long NextDeliveryId => nextDeliveryId;

    internal ApplicationReliabilityReceiveResult Accept(
        ApplicationReliablePacket frame,
        Packet packet) {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(packet);
        if (frame.DeliveryId < nextDeliveryId) {
            return new ApplicationReliabilityReceiveResult(
                ApplicationReliabilityReceiveDisposition.DuplicateDelivered,
                []);
        }
        if (buffered.ContainsKey(frame.DeliveryId)) {
            return new ApplicationReliabilityReceiveResult(
                ApplicationReliabilityReceiveDisposition.DuplicateBuffered,
                []);
        }
        if (frame.DeliveryId - nextDeliveryId > MaximumSequenceGap) {
            throw new InvalidDataException(
                $"Application delivery {frame.DeliveryId} is too far ahead of expected {nextDeliveryId}.");
        }
        if (buffered.Count >= MaximumBufferedDeliveries
            || frame.PayloadLength > MaximumBufferedBytes - Bytes) {
            throw new InvalidDataException(
                "Application-reliable receive window capacity was exceeded.");
        }

        buffered.Add(frame.DeliveryId, new BufferedDelivery(frame, packet));
        Bytes += frame.PayloadLength;
        List<BufferedDelivery> ready = [];
        while (buffered.Remove(nextDeliveryId, out BufferedDelivery delivery)) {
            Bytes -= delivery.Frame.PayloadLength;
            ready.Add(delivery);
            nextDeliveryId++;
        }
        return new ApplicationReliabilityReceiveResult(
            ready.Count > 0
                ? ApplicationReliabilityReceiveDisposition.Ready
                : ApplicationReliabilityReceiveDisposition.Buffered,
            ready);
    }

    internal void Clear() {
        buffered.Clear();
        Bytes = 0;
        nextDeliveryId = 1;
    }

    internal sealed record BufferedDelivery(
        ApplicationReliablePacket Frame,
        Packet Packet);
}

internal enum ApplicationReliabilityReceiveDisposition {
    Ready,
    Buffered,
    DuplicateDelivered,
    DuplicateBuffered
}

internal sealed record ApplicationReliabilityReceiveResult(
    ApplicationReliabilityReceiveDisposition Disposition,
    IReadOnlyList<ApplicationReliabilityReceiveWindow.BufferedDelivery> Ready);

internal sealed class ApplicationReliabilityPendingWindow {
    internal const int MaximumPendingDeliveries = 4096;
    internal const long MaximumPendingBytes = 64L * 1024 * 1024;

    private readonly Dictionary<long, PendingDelivery> pending = [];
    private readonly int maximumPendingDeliveries;
    private readonly long maximumPendingBytes;

    internal ApplicationReliabilityPendingWindow(
        int maximumPendingDeliveries = MaximumPendingDeliveries,
        long maximumPendingBytes = MaximumPendingBytes) {
        if (maximumPendingDeliveries <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingDeliveries));
        }
        if (maximumPendingBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingBytes));
        }
        this.maximumPendingDeliveries = maximumPendingDeliveries;
        this.maximumPendingBytes = maximumPendingBytes;
    }

    internal int Count => pending.Count;
    internal long Bytes { get; private set; }

    internal void Add(
        ApplicationReliablePacket frame,
        string logicalPacketType,
        double now) {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPacketType);
        int payloadLength = frame.PayloadLength;
        if (pending.Count >= maximumPendingDeliveries
            || payloadLength > maximumPendingBytes - Bytes) {
            throw new InvalidOperationException(
                "Application-reliable pending window capacity was exceeded.");
        }
        if (pending.ContainsKey(frame.DeliveryId)) {
            throw new InvalidOperationException(
                $"Application delivery {frame.DeliveryId} is already pending.");
        }
        pending.Add(
            frame.DeliveryId,
            new PendingDelivery(
                frame,
                logicalPacketType,
                now,
                now + ApplicationReliabilityRuntime.InitialRetryDelaySeconds));
        Bytes += payloadLength;
    }

    internal bool Acknowledge(long deliveryId) {
        if (!pending.Remove(deliveryId, out PendingDelivery delivery)) {
            return false;
        }
        Bytes -= delivery.Frame.PayloadLength;
        return true;
    }

    internal PendingDelivery[] GetDue(double now) => pending.Values
        .Where(delivery => delivery.NextAttemptAt <= now)
        .OrderBy(delivery => delivery.Frame.DeliveryId)
        .ToArray();

    internal void MarkRetried(long deliveryId, double now) {
        if (!pending.TryGetValue(deliveryId, out PendingDelivery delivery)) {
            return;
        }
        delivery.Attempts++;
        double delay = Math.Min(
            ApplicationReliabilityRuntime.InitialRetryDelaySeconds
                * Math.Pow(2, Math.Min(delivery.Attempts - 1, 16)),
            ApplicationReliabilityRuntime.MaximumRetryDelaySeconds);
        delivery.NextAttemptAt = now + delay;
    }

    internal PendingDelivery[] RemoveTimedOut(double now) {
        PendingDelivery[] timedOut = pending.Values
            .Where(delivery =>
                now - delivery.CreatedAt
                    >= ApplicationReliabilityRuntime.DeliveryTimeoutSeconds)
            .ToArray();
        foreach (PendingDelivery delivery in timedOut) {
            Acknowledge(delivery.Frame.DeliveryId);
        }
        return timedOut;
    }

    internal void Clear() {
        pending.Clear();
        Bytes = 0;
    }

    internal sealed class PendingDelivery(
        ApplicationReliablePacket frame,
        string logicalPacketType,
        double createdAt,
        double nextAttemptAt) {

        internal ApplicationReliablePacket Frame { get; } = frame;
        internal string LogicalPacketType { get; } = logicalPacketType;
        internal double CreatedAt { get; } = createdAt;
        internal double NextAttemptAt { get; set; } = nextAttemptAt;
        internal int Attempts { get; set; } = 1;
    }
}

public static class ApplicationReliabilityRuntime {
    internal const double InitialRetryDelaySeconds = 0.25;
    internal const double MaximumRetryDelaySeconds = 2.0;
    internal const double DeliveryTimeoutSeconds = 30.0;

    private static long[] clientNextDeliveryIds = new long[NetworkChannels.Count];
    private static ApplicationReliabilityPendingWindow[] clientPending =
        CreatePendingWindows();
    private static ApplicationReliabilityReceiveWindow[] clientReceived =
        CreateReceiveWindows();
    private static readonly Dictionary<Guid, ServerPeerState> serverPeers = [];
    private static long created;
    private static long retried;
    private static long acknowledged;
    private static long duplicateDeliveries;
    private static long timedOut;
    private static long failed;
    private static string pendingClientFailure = string.Empty;
    private static readonly Dictionary<Guid, PendingServerFailure> pendingServerFailures = [];

    internal static int PendingFailureCount =>
        (string.IsNullOrEmpty(pendingClientFailure) ? 0 : 1)
        + pendingServerFailures.Count;

    public static ApplicationReliabilityMetrics Metrics {
        get {
            int pending = clientPending.Sum(window => window.Count);
            long bytes = clientPending.Sum(window => window.Bytes);
            foreach (ServerPeerState state in serverPeers.Values) {
                pending += state.Pending.Sum(window => window.Count);
                bytes += state.Pending.Sum(window => window.Bytes);
            }
            return new ApplicationReliabilityMetrics(
                created,
                retried,
                acknowledged,
                duplicateDeliveries,
                timedOut,
                failed,
                pending,
                bytes);
        }
    }

    internal static Packet ProtectClient(Packet packet, ClientSession session) {
        if (!ApplicationReliabilityPolicy.ShouldProtect(packet, session)) {
            return packet;
        }
        if (!string.IsNullOrEmpty(pendingClientFailure)) {
            return null;
        }
        ApplicationReliablePacket frame = CreateFrame(
            packet,
            Interlocked.Increment(ref clientNextDeliveryIds[packet.Channel]));
        try {
            clientPending[packet.Channel].Add(
                frame,
                packet.GetType().Name,
                Time.RealTime);
        }
        catch (Exception exception) {
            FailClosed(
                isServer: false,
                session,
                "outbound pending window rejected a delivery",
                exception);
            return null;
        }
        created++;
        return frame;
    }

    internal static Packet ProtectServer(Packet packet, ClientSession session) {
        if (!ApplicationReliabilityPolicy.ShouldProtect(packet, session)) {
            return packet;
        }
        if (pendingServerFailures.ContainsKey(session.SessionId)) {
            return null;
        }
        ServerPeerState peer = GetServerPeer(session);
        ApplicationReliablePacket frame = CreateFrame(
            packet,
            Interlocked.Increment(ref peer.NextDeliveryIds[packet.Channel]));
        try {
            peer.Pending[packet.Channel].Add(
                frame,
                packet.GetType().Name,
                Time.RealTime);
        }
        catch (Exception exception) {
            FailClosed(
                isServer: true,
                session,
                "outbound pending window rejected a delivery",
                exception);
            return null;
        }
        created++;
        return frame;
    }

    internal static void Handle(ApplicationReliablePacket frame, bool isServer) {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Kind == ApplicationReliableFrameKind.Acknowledgement) {
            if (isServer && frame.From == null) {
                FailClosed(
                    isServer: true,
                    session: null,
                    "acknowledgement had no source session",
                    new InvalidDataException(
                        "A server-side acknowledgement must have a source session."));
                return;
            }
            bool removed = isServer
                ? GetServerPeer(frame.From).Pending[frame.OriginalChannel]
                    .Acknowledge(frame.DeliveryId)
                : clientPending[frame.OriginalChannel]
                    .Acknowledge(frame.DeliveryId);
            if (removed) {
                acknowledged++;
            }
            return;
        }

        if (isServer && frame.From == null) {
            return;
        }
        ApplicationReliabilityReceiveResult result;
        Packet inner;
        try {
            ApplicationReliabilityReceiveWindow receiveWindow = isServer
                ? GetServerPeer(frame.From).Received[frame.OriginalChannel]
                : clientReceived[frame.OriginalChannel];
            inner = DecodeAndValidate(frame);
            if (isServer) {
                inner.From = frame.From;
            }
            result = receiveWindow.Accept(frame, inner);
        }
        catch (Exception exception) {
            FailClosed(
                isServer,
                frame.From,
                "inbound frame was rejected",
                exception);
            return;
        }
        QueueAcknowledgement(frame, isServer);
        if (result.Disposition is ApplicationReliabilityReceiveDisposition.DuplicateDelivered
            or ApplicationReliabilityReceiveDisposition.DuplicateBuffered) {
            duplicateDeliveries++;
            if (result.Disposition
                    == ApplicationReliabilityReceiveDisposition.DuplicateDelivered
                && ApplicationReliabilityPolicy
                    .ShouldReplayDuplicateForDomainEvidence(inner)) {
                if (!TryHandle(inner, frame.From, isServer)) {
                    return;
                }
            }
            return;
        }
        foreach (ApplicationReliabilityReceiveWindow.BufferedDelivery delivery
                 in result.Ready) {
            if (!TryHandle(delivery.Packet, frame.From, isServer)) {
                return;
            }
        }
    }

    public static void Update() {
        (bool clientFailed, HashSet<Guid> failedServerSessions) =
            DrainPendingFailures();
        double now = Time.RealTime;
        bool clientTimedOut = !clientFailed && RetryClient(now);
        List<ClientSession> serverTimeouts = [];
        foreach (ServerPeerState state in serverPeers.Values.ToArray()) {
            if (!failedServerSessions.Contains(state.Session.SessionId)
                && RetryServer(state, now)) {
                serverTimeouts.Add(state.Session);
            }
        }
        if (clientTimedOut) {
            Log.Warning(
                "Application-reliable delivery timed out; closing the client session to avoid silent divergence.");
            NetworkManager.StopClient();
        }
        foreach (ClientSession session in serverTimeouts.Distinct()) {
            Log.Warning(
                $"Application-reliable delivery timed out for session {session.SessionId}; disconnecting it.");
            NetworkManager.DisconnectSession(
                session,
                "Application-reliable delivery timed out.");
        }
    }

    public static void ClearClient() {
        foreach (ApplicationReliabilityPendingWindow window in clientPending) {
            window.Clear();
        }
        foreach (ApplicationReliabilityReceiveWindow window in clientReceived) {
            window.Clear();
        }
        Array.Clear(clientNextDeliveryIds, 0, clientNextDeliveryIds.Length);
        pendingClientFailure = string.Empty;
    }

    public static void ClearServerSession(ClientSession session) {
        if (session != null) {
            serverPeers.Remove(session.SessionId);
            pendingServerFailures.Remove(session.SessionId);
        }
    }

    public static void Reset() {
        clientNextDeliveryIds = new long[NetworkChannels.Count];
        clientPending = CreatePendingWindows();
        clientReceived = CreateReceiveWindows();
        serverPeers.Clear();
        created = 0;
        retried = 0;
        acknowledged = 0;
        duplicateDeliveries = 0;
        timedOut = 0;
        failed = 0;
        pendingClientFailure = string.Empty;
        pendingServerFailures.Clear();
    }

    private static ApplicationReliablePacket CreateFrame(
        Packet packet,
        long deliveryId) =>
        ApplicationReliablePacket.CreateData(
            deliveryId,
            packet.Channel,
            packet.MinNeedState,
            packet.RequiresProject,
            PacketManager.EncodeRawPacket(packet));

    private static Packet DecodeAndValidate(ApplicationReliablePacket frame) {
        Packet inner = PacketManager.DecodeRawPacket(frame.Payload);
        if (inner is ApplicationReliablePacket
            || inner.Channel != frame.OriginalChannel
            || inner.MinNeedState != frame.OriginalMinNeedState
            || inner.RequiresProject != frame.OriginalRequiresProject) {
            throw new InvalidDataException(
                "Application-reliable frame metadata does not match its inner packet.");
        }
        return inner;
    }

    private static bool TryHandle(
        Packet packet,
        ClientSession session,
        bool isServer) {
        try {
            packet.Handle(isServer);
            return true;
        }
        catch (Exception exception) {
            FailClosed(
                isServer,
                session,
                $"inner {packet.GetType().Name} handler failed",
                exception);
            return false;
        }
    }

    private static void FailClosed(
        bool isServer,
        ClientSession session,
        string context,
        Exception exception) {
        if (isServer && session != null
            && pendingServerFailures.ContainsKey(session.SessionId)) {
            return;
        }
        if (!isServer && !string.IsNullOrEmpty(pendingClientFailure)) {
            return;
        }
        failed++;
        string reason = $"Application-reliable {context}: {exception.Message}";
        Log.Warning($"{reason}{Environment.NewLine}{exception}");
        if (isServer) {
            if (session != null) {
                pendingServerFailures[session.SessionId] =
                    new PendingServerFailure(session, reason);
            }
        }
        else if (string.IsNullOrEmpty(pendingClientFailure)) {
            pendingClientFailure = reason;
        }
    }

    private static (bool ClientFailed, HashSet<Guid> ServerSessions)
        DrainPendingFailures() {
        bool clientFailed = !string.IsNullOrEmpty(pendingClientFailure);
        string clientReason = pendingClientFailure;
        pendingClientFailure = string.Empty;
        PendingServerFailure[] serverFailures = pendingServerFailures.Values.ToArray();
        pendingServerFailures.Clear();

        if (clientFailed) {
            Log.Warning($"{clientReason}; closing the client session to avoid divergence.");
            NetworkManager.StopClient();
        }
        HashSet<Guid> failedSessions = [];
        foreach (PendingServerFailure failure in serverFailures) {
            failedSessions.Add(failure.Session.SessionId);
            NetworkManager.DisconnectSession(failure.Session, failure.Reason);
        }
        return (clientFailed, failedSessions);
    }

    private static ServerPeerState GetServerPeer(ClientSession session) {
        ArgumentNullException.ThrowIfNull(session);
        if (!serverPeers.TryGetValue(session.SessionId, out ServerPeerState state)) {
            state = new ServerPeerState(session);
            serverPeers.Add(session.SessionId, state);
        }
        return state;
    }

    private static void QueueAcknowledgement(
        ApplicationReliablePacket frame,
        bool isServer) {
        ApplicationReliablePacket acknowledgement =
            ApplicationReliablePacket.CreateAcknowledgement(
                frame.DeliveryId,
                frame.OriginalChannel);
        if (isServer) {
            acknowledgement.To = frame.From;
        }
        NetworkManager.Queue(acknowledgement);
    }

    private static bool RetryClient(double now) {
        List<ApplicationReliabilityPendingWindow.PendingDelivery> expired = [];
        foreach (ApplicationReliabilityPendingWindow window in clientPending) {
            foreach (ApplicationReliabilityPendingWindow.PendingDelivery delivery
                     in window.GetDue(now)) {
                bool sent = (NetworkManager.NetworkClient as NetworkClient)
                    ?.SendPrepared(delivery.Frame, delivery.LogicalPacketType) == true;
                sent |= (NetworkManager.RelayClient as RelayClient)
                    ?.SendPrepared(delivery.Frame, delivery.LogicalPacketType) == true;
                window.MarkRetried(delivery.Frame.DeliveryId, now);
                if (sent) {
                    retried++;
                }
            }
            expired.AddRange(window.RemoveTimedOut(now));
        }
        foreach (ApplicationReliabilityPendingWindow.PendingDelivery delivery in expired) {
            Log.Warning(
                $"Client application delivery {delivery.Frame.DeliveryId} "
                + $"({delivery.LogicalPacketType}) timed out after {delivery.Attempts} attempts.");
        }
        timedOut += expired.Count;
        return expired.Count > 0;
    }

    private static bool RetryServer(ServerPeerState state, double now) {
        List<ApplicationReliabilityPendingWindow.PendingDelivery> expired = [];
        foreach (ApplicationReliabilityPendingWindow window in state.Pending) {
            foreach (ApplicationReliabilityPendingWindow.PendingDelivery delivery
                     in window.GetDue(now)) {
                bool sent = (NetworkManager.NetworkServer as NetworkServer)
                    ?.SendPrepared(
                        delivery.Frame,
                        state.Session,
                        delivery.LogicalPacketType) == true;
                sent |= (NetworkManager.RelayHost as RelayHost)
                    ?.SendPrepared(
                        delivery.Frame,
                        state.Session,
                        delivery.LogicalPacketType) == true;
                window.MarkRetried(delivery.Frame.DeliveryId, now);
                if (sent) {
                    retried++;
                }
            }
            expired.AddRange(window.RemoveTimedOut(now));
        }
        foreach (ApplicationReliabilityPendingWindow.PendingDelivery delivery in expired) {
            Log.Warning(
                $"Server application delivery {delivery.Frame.DeliveryId} "
                + $"({delivery.LogicalPacketType}) timed out after {delivery.Attempts} attempts "
                + $"for session {state.Session.SessionId}.");
        }
        timedOut += expired.Count;
        return expired.Count > 0;
    }

    private static ApplicationReliabilityPendingWindow[] CreatePendingWindows() =>
        Enumerable.Range(0, NetworkChannels.Count)
            .Select(_ => new ApplicationReliabilityPendingWindow())
            .ToArray();

    private static ApplicationReliabilityReceiveWindow[] CreateReceiveWindows() =>
        Enumerable.Range(0, NetworkChannels.Count)
            .Select(_ => new ApplicationReliabilityReceiveWindow())
            .ToArray();

    private sealed class ServerPeerState(ClientSession session) {
        internal ClientSession Session { get; } = session;
        internal ApplicationReliabilityPendingWindow[] Pending { get; } =
            CreatePendingWindows();
        internal ApplicationReliabilityReceiveWindow[] Received { get; } =
            CreateReceiveWindows();
        internal long[] NextDeliveryIds { get; } = new long[NetworkChannels.Count];
    }

    private sealed record PendingServerFailure(
        ClientSession Session,
        string Reason);
}
