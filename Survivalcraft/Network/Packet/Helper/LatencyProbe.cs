using System;
using Engine;

namespace Game.Network;

public static class LatencyProbe {
    private const double ProbeInterval = 1.0;
    private const double ProbeTimeout = 5.0;

    private static int m_sequence;
    private static int m_pendingSequence = -1;
    private static double m_sentTime;
    private static double m_lastProbeTime;

    public static int Latency { get; private set; } = -1;

    public static void Update() {
        if (NetworkManager.MySession?.NetworkState < NetworkState.Connected) {
            return;
        }

        double now = Time.RealTime;
        if (m_pendingSequence >= 0 && now - m_sentTime > ProbeTimeout) {
            m_pendingSequence = -1;
            Latency = -1;
        }
        if (now - m_lastProbeTime < ProbeInterval || m_pendingSequence >= 0) {
            return;
        }

        int sequence = ++m_sequence;
        m_pendingSequence = sequence;
        m_sentTime = now;
        m_lastProbeTime = now;
        NetworkManager.Queue(new LatencyProbePacket(sequence, now, false));
    }

    public static void ReceiveReply(int sequence, double clientSentTime) {
        if (sequence != m_pendingSequence) {
            return;
        }
        double elapsed = Math.Max(0.0, Time.RealTime - clientSentTime);
        Latency = (int)Math.Round(elapsed * 1000.0);
        m_pendingSequence = -1;
    }

    public static void Reset() {
        m_pendingSequence = -1;
        m_sentTime = 0.0;
        m_lastProbeTime = 0.0;
        Latency = -1;
    }
}
