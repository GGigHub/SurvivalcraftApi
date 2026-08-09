using Game;
using Game.Network;
using Engine;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int FaultInjectionSettleFrames = 30;
    private static long applicationReliabilityProbeNonce;

    // This file is the only permitted direct NetworkManager.Queue boundary in
    // TestAutomation. Call-site naming makes fixtures, production traffic,
    // authoritative state and deliberate fault injection distinguishable in review.
    private static void QueueFixtureSynchronization(object packet) =>
        QueueClassifiedPacket(packet);

    private static void QueueProductionRequest(object packet) =>
        QueueClassifiedPacket(packet);

    private static void QueueAuthoritativeState(object packet) =>
        QueueClassifiedPacket(packet);

    private static void QueueFaultInjection(object packet) =>
        QueueClassifiedPacket(packet);

    private static void QueueApplicationReliabilityProbe() =>
        QueueFaultInjection(new ApplicationReliabilityProbePacket(
            Interlocked.Increment(ref applicationReliabilityProbeNonce)));

    private static void QueueFaultInjectionAfterSettle(object packet) {
        ArgumentNullException.ThrowIfNull(packet);
        int deliveryFrame = checked(Time.FrameIndex + FaultInjectionSettleFrames);
        Time.QueueFrameIndexDelayedExecution(
            deliveryFrame,
            () => QueueFaultInjection(packet));
    }

    private static void QueueClassifiedPacket(object packet) {
        ArgumentNullException.ThrowIfNull(packet);
        NetworkManager.Queue(packet);
    }
}
