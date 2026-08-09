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

    [Fact]
    [Trait("Layer", "L0")]
    public void PacketTypeFilterAppliesFaultsOnlyToNamedPackets() {
        int delivered = 0;
        try {
            NetworkFaultInjector.Configure(new NetworkFaultSettings(
                LossRate: 1,
                TargetPacketTypes: [nameof(CompatibilityHelloPacket)]));
            NetDataWriter targeted = new();
            targeted.Put((byte)1);
            NetworkFaultInjector.Send(
                targeted,
                _ => delivered++,
                nameof(CompatibilityHelloPacket));
            NetDataWriter bypassed = new();
            bypassed.Put((byte)2);
            NetworkFaultInjector.Send(
                bypassed,
                _ => delivered++,
                nameof(StatePacket));

            Assert.Equal(1, delivered);
            Assert.Equal(1, NetworkFaultInjector.Metrics.Dropped);
            Assert.Equal(1, NetworkFaultInjector.Metrics.Sent);
            Assert.Equal(
                new[] { nameof(CompatibilityHelloPacket) },
                NetworkFaultInjector.Settings.TargetPacketTypes);
        }
        finally {
            NetworkFaultInjector.Configure(new NetworkFaultSettings());
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LossSoakGuaranteeSkipsAcknowledgementsAndDropsReliableData() {
        int delivered = 0;
        try {
            NetworkFaultInjector.Configure(new NetworkFaultSettings(
                Seed: 7,
                LossRate: 0.01,
                TargetPacketTypes: [nameof(CompatibilityHelloPacket)],
                GuaranteeReliableLoss: true));
            NetDataWriter acknowledgement = new();
            acknowledgement.Put((byte)0);
            NetworkFaultInjector.Send(
                acknowledgement,
                _ => delivered++,
                nameof(StatePacket),
                applicationReliableData: false);
            Assert.Equal(0, NetworkFaultInjector.Metrics.Dropped);
            Assert.Equal(1, delivered);
            Assert.Equal(0, NetworkFaultInjector.Metrics.Pending);
            NetDataWriter writer = new();
            writer.Put((byte)1);

            NetworkFaultInjector.Send(
                writer,
                _ => delivered++,
                nameof(StatePacket),
                applicationReliableData: true);

            Assert.Equal(1, delivered);
            Assert.Equal(1, NetworkFaultInjector.Metrics.Dropped);
        }
        finally {
            NetworkFaultInjector.Configure(new NetworkFaultSettings());
        }
    }
}
