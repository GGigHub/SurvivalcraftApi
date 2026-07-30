using System;

namespace Game.Network;

public static class NetworkSessionAdmissionRuntime {
    public static readonly TimeSpan ClientAdmissionTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ServerAdmissionTimeout = TimeSpan.FromSeconds(20);

    private static NetworkSessionAdmissionResult s_pendingClientRejection;

    public static NetworkSessionAdmissionResult LastClientResult { get; private set; }

    public static void BeginClientAdmission(ClientSession session) {
        ArgumentNullException.ThrowIfNull(session);
        session.AdmissionState = NetworkSessionAdmissionState.AwaitingRequest;
        session.AdmissionResult = null;
        session.AdmissionStartedUtc = DateTime.UtcNow;
        LastClientResult = null;
        s_pendingClientRejection = null;
    }

    public static void BeginServerAdmission(ClientSession session) {
        ArgumentNullException.ThrowIfNull(session);
        session.AdmissionState = NetworkSessionAdmissionState.AwaitingRequest;
        session.AdmissionResult = null;
        session.AdmissionStartedUtc = DateTime.UtcNow;
    }

    public static void UpdateClientAdmission(ClientSession session) {
        if (session == null
            || session.AdmissionState != NetworkSessionAdmissionState.AwaitingRequest
            || session.AdmissionStartedUtc == default
            || DateTime.UtcNow - session.AdmissionStartedUtc < ClientAdmissionTimeout) {
            return;
        }
        RecordClientResult(
            session,
            NetworkSessionAdmission.CreateTimeoutResult(
                (int)ClientAdmissionTimeout.TotalSeconds));
    }

    public static bool HasServerTimedOut(ClientSession session, DateTime now) =>
        session != null
        && session.CompatibilityState == NetworkCompatibilityState.Accepted
        && session.AdmissionState == NetworkSessionAdmissionState.AwaitingRequest
        && session.AdmissionStartedUtc != default
        && now - session.AdmissionStartedUtc >= ServerAdmissionTimeout;

    public static void RecordClientResult(
        ClientSession session,
        NetworkSessionAdmissionResult result) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        session.AdmissionResult = result;
        session.AdmissionState = result.Accepted
            ? NetworkSessionAdmissionState.Accepted
            : NetworkSessionAdmissionState.Rejected;
        session.AdmissionStartedUtc = default;
        LastClientResult = result;
        if (result.Accepted) {
            NetworkSessionLifecycleTracker.RecordAdmissionAccepted(session);
        }
        else {
            s_pendingClientRejection = result;
            NetworkSessionLifecycleTracker.RecordAdmissionRejected(session, result.Code.ToString());
        }
    }

    public static void RejectServerSession(
        ClientSession session,
        NetworkSessionAdmissionResult result) {
        if (session == null) {
            return;
        }
        session.AdmissionResult = result;
        session.AdmissionState = NetworkSessionAdmissionState.Rejected;
        session.AdmissionStartedUtc = default;
        NetworkSessionLifecycleTracker.RecordAdmissionRejected(session, result.Code.ToString());
        NetworkManager.Queue(new SessionAdmissionResultPacket(result) { To = session });
    }

    public static void RejectPrematurePacket(ClientSession session, Packet packet) =>
        RejectServerSession(
            session,
            NetworkSessionAdmission.CreateRequiredResult(
                packet?.GetType().Name ?? "unknown packet"));

    public static bool TryTakePendingClientRejection(
        out NetworkSessionAdmissionResult result) {
        result = s_pendingClientRejection;
        s_pendingClientRejection = null;
        return result != null;
    }
}
