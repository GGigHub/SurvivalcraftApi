using System;
using System.Collections.Generic;

namespace Game.Network;

public sealed record NetworkSessionLifecycleEvent(
    long Sequence,
    DateTime TimestampUtc,
    Guid SessionId,
    string TransportKind,
    string Kind,
    Guid NetworkGuid,
    int PlayerIndex,
    string Detail);

public readonly record struct NetworkSessionLifecycleMetrics(
    long Connected,
    long CompatibilityAccepted,
    long CompatibilityRejected,
    long AdmissionAccepted,
    long AdmissionRejected,
    long Superseded,
    long PlayerAssigned,
    long DuplicatePlayerCreateIgnored,
    long PlayerEntityRequested,
    long Disconnected,
    NetworkSessionLifecycleEvent[] Events);

public static class NetworkSessionLifecycleTracker {
    private const int MaximumEvents = 512;
    private static readonly object s_sync = new();
    private static readonly Queue<NetworkSessionLifecycleEvent> s_events = new();
    private static long s_sequence;
    private static long s_connected;
    private static long s_compatibilityAccepted;
    private static long s_compatibilityRejected;
    private static long s_admissionAccepted;
    private static long s_admissionRejected;
    private static long s_superseded;
    private static long s_playerAssigned;
    private static long s_duplicatePlayerCreateIgnored;
    private static long s_playerEntityRequested;
    private static long s_disconnected;

    public static NetworkSessionLifecycleMetrics Metrics {
        get {
            lock (s_sync) {
                return new NetworkSessionLifecycleMetrics(
                    s_connected,
                    s_compatibilityAccepted,
                    s_compatibilityRejected,
                    s_admissionAccepted,
                    s_admissionRejected,
                    s_superseded,
                    s_playerAssigned,
                    s_duplicatePlayerCreateIgnored,
                    s_playerEntityRequested,
                    s_disconnected,
                    s_events.ToArray());
            }
        }
    }

    public static void Reset() {
        lock (s_sync) {
            s_sequence = 0;
            s_connected = 0;
            s_compatibilityAccepted = 0;
            s_compatibilityRejected = 0;
            s_admissionAccepted = 0;
            s_admissionRejected = 0;
            s_superseded = 0;
            s_playerAssigned = 0;
            s_duplicatePlayerCreateIgnored = 0;
            s_playerEntityRequested = 0;
            s_disconnected = 0;
            s_events.Clear();
        }
    }

    public static void RecordConnected(ClientSession session) =>
        Record(session, "connected", string.Empty, ref s_connected);

    public static void RecordCompatibilityAccepted(ClientSession session) =>
        Record(session, "compatibility-accepted", string.Empty, ref s_compatibilityAccepted);

    public static void RecordCompatibilityRejected(ClientSession session, string detail) =>
        Record(session, "compatibility-rejected", detail, ref s_compatibilityRejected);

    public static void RecordAdmissionAccepted(ClientSession session) =>
        Record(session, "admission-accepted", string.Empty, ref s_admissionAccepted);

    public static void RecordAdmissionRejected(ClientSession session, string detail) =>
        Record(session, "admission-rejected", detail, ref s_admissionRejected);

    public static void RecordSuperseded(ClientSession session) =>
        Record(session, "superseded", string.Empty, ref s_superseded);

    public static void RecordPlayerAssigned(ClientSession session) =>
        Record(session, "player-assigned", string.Empty, ref s_playerAssigned);

    public static void RecordDuplicatePlayerCreateIgnored(ClientSession session) =>
        Record(
            session,
            "duplicate-player-create-ignored",
            string.Empty,
            ref s_duplicatePlayerCreateIgnored);

    public static void RecordPlayerEntityRequested(ClientSession session) =>
        Record(session, "player-entity-requested", string.Empty, ref s_playerEntityRequested);

    public static void RecordDisconnected(ClientSession session, string detail) =>
        Record(session, "disconnected", detail, ref s_disconnected);

    private static void Record(
        ClientSession session,
        string kind,
        string detail,
        ref long counter) {
        if (session == null) {
            return;
        }
        lock (s_sync) {
            counter++;
            s_events.Enqueue(new NetworkSessionLifecycleEvent(
                ++s_sequence,
                DateTime.UtcNow,
                session.SessionId,
                session.TransportKind ?? string.Empty,
                kind,
                session.NetworkGuid,
                session.PlayerIndex,
                detail ?? string.Empty));
            while (s_events.Count > MaximumEvents) {
                s_events.Dequeue();
            }
        }
    }
}
