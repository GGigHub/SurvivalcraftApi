using System.Reflection;
using Engine;
using Game.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NetworkFaultInjectorTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void PacketTrafficEvidenceIsTypedAndResetsWithTheFaultProfile() {
        try {
            NetworkFaultInjector.Configure(new NetworkFaultSettings(
                BaseLatencyMilliseconds: 1));
            NetworkPacketTrafficTracker.RecordQueued(new StatePacket());
            NetworkPacketTrafficTracker.RecordQueued(new StatePacket());
            NetDataWriter writer = new();
            writer.Put((byte)1);
            NetworkFaultInjector.Send(
                writer,
                static _ => { },
                nameof(StatePacket));

            NetworkPacketTrafficMetrics metrics = NetworkPacketTrafficTracker.Metrics;
            Assert.Equal(2, metrics.QueuedPacketsByType[nameof(StatePacket)]);
            Assert.Equal(1, metrics.FaultTransmissionsByType[nameof(StatePacket)]);
            Assert.True(metrics.FaultBytesByType[nameof(StatePacket)] > 0);

            NetworkFaultInjector.Configure(new NetworkFaultSettings());
            Assert.Empty(NetworkPacketTrafficTracker.Metrics.QueuedPacketsByType);
        }
        finally {
            NetworkFaultInjector.Configure(new NetworkFaultSettings());
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void BandwidthLimitDoesNotInventAPerPacketMillisecondFloor() {
        const int packetCount = 2000;
        const int bandwidthBytesPerSecond = 1_000_000;
        FieldInfo linkAvailableAtField = typeof(NetworkFaultInjector).GetField(
            "m_linkAvailableAt",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(NetworkFaultInjector), "m_linkAvailableAt");

        try {
            NetworkFaultInjector.Configure(new NetworkFaultSettings(
                BandwidthBytesPerSecond: bandwidthBytesPerSecond));
            double startedAt = Time.RealTime;
            for (int index = 0; index < packetCount; index++) {
                NetDataWriter writer = new();
                writer.Put((byte)index);
                NetworkFaultInjector.Send(writer, static _ => { });
            }

            double linkAvailableAt = (double)linkAvailableAtField.GetValue(null)!;
            double scheduledSpanSeconds = linkAvailableAt - startedAt;

            Assert.Equal(packetCount, NetworkFaultInjector.Metrics.Pending);
            Assert.InRange(scheduledSpanSeconds, 0, 0.1);
        }
        finally {
            NetworkFaultInjector.Configure(new NetworkFaultSettings());
        }
    }
}
