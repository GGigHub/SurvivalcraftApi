using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

[Collection(ModNetworkManagerCollection.Name)]
public sealed class ApplicationReliabilityTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void ReliableDataAndAcknowledgementHaveBoundedRoundTripWireShapes() {
        PacketManager.Initialize();
        StatePacket inner = new(7, NetworkState.Playing);
        ApplicationReliablePacket expected = ApplicationReliablePacket.CreateData(
            41,
            inner.Channel,
            inner.MinNeedState,
            inner.RequiresProject,
            PacketManager.EncodeRawPacket(inner));

        ApplicationReliablePacket actual = RoundTrip(expected);
        StatePacket decodedInner = Assert.IsType<StatePacket>(
            PacketManager.DecodeRawPacket(actual.Payload));

        Assert.Equal(ApplicationReliableFrameKind.Data, actual.Kind);
        Assert.Equal(41, actual.DeliveryId);
        Assert.Equal(NetworkState.NotConnected, actual.OriginalMinNeedState);
        Assert.False(actual.OriginalRequiresProject);
        Assert.Equal(7, decodedInner.m_playerIndex);
        Assert.Equal(NetworkState.Playing, decodedInner.m_state);

        ApplicationReliablePacket acknowledgement = RoundTrip(
            ApplicationReliablePacket.CreateAcknowledgement(
                41,
                NetworkChannels.Terrain));
        Assert.Equal(
            ApplicationReliableFrameKind.Acknowledgement,
            acknowledgement.Kind);
        Assert.Empty(acknowledgement.Payload);
        Assert.Equal(NetworkChannels.Terrain, acknowledgement.Channel);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ReliableInnerPacketRejectsTrailingData() {
        PacketManager.Initialize();
        byte[] payload = PacketManager.EncodeRawPacket(
            new StatePacket(7, NetworkState.Playing));

        Assert.Throws<InvalidDataException>(
            () => PacketManager.DecodeRawPacket([.. payload, 0x7f]));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ReliablePolicyProtectsWorldMessagesButLeavesReplaceableStreamsAlone() {
        ApplicationReliabilityRuntime.Reset();
        ClientSession accepted = new() {
            AdmissionState = NetworkSessionAdmissionState.Accepted
        };
        StatePacket state = new(3, NetworkState.ProjectLoaded);

        Packet protectedState = ApplicationReliabilityRuntime.ProtectClient(
            state,
            accepted);

        Assert.IsType<ApplicationReliablePacket>(protectedState);
        Assert.Equal(1, ApplicationReliabilityRuntime.Metrics.Pending);
        PlayerBodyUpdatePacket bodyUpdate = new();
        Assert.Same(
            bodyUpdate,
            ApplicationReliabilityRuntime.ProtectClient(bodyUpdate, accepted));
        PickableUpdatePacket pickableUpdate = new();
        Assert.Same(
            pickableUpdate,
            ApplicationReliabilityRuntime.ProtectClient(
                pickableUpdate,
                accepted));
        Assert.IsNotType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new SubsystemElectricityPacket(),
                accepted));
        Assert.IsNotType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new PlayerDigPacket(),
                accepted));
        Assert.IsNotType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new EntitySyncRequestPacket([17]),
                accepted));
        Assert.True(ApplicationReliabilityPolicy.ShouldProtect(
            new HealthSyncPacket(),
            accepted));
        ApplicationReliablePacket protectedProbe = Assert.IsType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new ApplicationReliabilityProbePacket(17),
                accepted));
        ApplicationReliablePacket protectedEntity = Assert.IsType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new EntityLoadPacket([]),
                accepted));
        Assert.Same(
            state,
            ApplicationReliabilityRuntime.ProtectClient(
                state,
                new ClientSession {
                    AdmissionState = NetworkSessionAdmissionState.AwaitingRequest
                }));

        ApplicationReliabilityRuntime.Handle(
            ApplicationReliablePacket.CreateAcknowledgement(
                Assert.IsType<ApplicationReliablePacket>(protectedState).DeliveryId,
                state.Channel),
            isServer: false);
        ApplicationReliabilityRuntime.Handle(
            ApplicationReliablePacket.CreateAcknowledgement(
                protectedEntity.DeliveryId,
                protectedEntity.Channel),
            isServer: false);
        ApplicationReliabilityRuntime.Handle(
            ApplicationReliablePacket.CreateAcknowledgement(
                protectedProbe.DeliveryId,
                protectedProbe.Channel),
            isServer: false);
        Assert.Equal(0, ApplicationReliabilityRuntime.Metrics.Pending);
        Assert.Equal(3, ApplicationReliabilityRuntime.Metrics.Acknowledged);
        ApplicationReliabilityRuntime.Reset();
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DeliverySequenceAndAcknowledgementAreIndependentPerTransportChannel() {
        ApplicationReliabilityRuntime.Reset();
        ClientSession accepted = new() {
            AdmissionState = NetworkSessionAdmissionState.Accepted
        };
        ApplicationReliablePacket defaultFrame = Assert.IsType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new ChannelPacket(NetworkChannels.Default),
                accepted));
        ApplicationReliablePacket terrainFrame = Assert.IsType<ApplicationReliablePacket>(
            ApplicationReliabilityRuntime.ProtectClient(
                new ChannelPacket(NetworkChannels.Terrain),
                accepted));

        Assert.Equal(1, defaultFrame.DeliveryId);
        Assert.Equal(1, terrainFrame.DeliveryId);
        Assert.Equal(2, ApplicationReliabilityRuntime.Metrics.Pending);
        ApplicationReliabilityRuntime.Handle(
            ApplicationReliablePacket.CreateAcknowledgement(
                defaultFrame.DeliveryId,
                defaultFrame.Channel),
            isServer: false);
        Assert.Equal(1, ApplicationReliabilityRuntime.Metrics.Pending);
        ApplicationReliabilityRuntime.Handle(
            ApplicationReliablePacket.CreateAcknowledgement(
                terrainFrame.DeliveryId,
                terrainFrame.Channel),
            isServer: false);
        Assert.Equal(0, ApplicationReliabilityRuntime.Metrics.Pending);
        ApplicationReliabilityRuntime.Reset();
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void OnlyDomainIdempotentPacketsObserveReliableDuplicates() {
        Assert.True(ApplicationReliabilityPolicy.ShouldReplayDuplicateForDomainEvidence(
            new ModMessagePacket()));
        Assert.True(ApplicationReliabilityPolicy.ShouldReplayDuplicateForDomainEvidence(
            new ProjectilePacket()));
        Assert.True(ApplicationReliabilityPolicy.ShouldReplayDuplicateForDomainEvidence(
            new ExplosionPacket()));
        Assert.False(ApplicationReliabilityPolicy.ShouldReplayDuplicateForDomainEvidence(
            new StatePacket()));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PendingWindowRetriesExponentiallyAndExpiresWithinHardBounds() {
        ApplicationReliabilityPendingWindow window = new();
        ApplicationReliablePacket frame = ApplicationReliablePacket.CreateData(
            9,
            NetworkChannels.Default,
            NetworkState.Playing,
            true,
            [0x88]);
        window.Add(frame, "StatePacket", now: 0);

        Assert.Empty(window.GetDue(0.249));
        Assert.Single(window.GetDue(0.25));
        window.MarkRetried(9, 0.25);
        Assert.Empty(window.GetDue(0.749));
        Assert.Single(window.GetDue(0.75));
        Assert.Single(window.RemoveTimedOut(
            ApplicationReliabilityRuntime.DeliveryTimeoutSeconds));
        Assert.Equal(0, window.Count);
        Assert.Equal(0, window.Bytes);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PendingWindowRejectsCountAndByteCapacityOverflow() {
        ApplicationReliabilityPendingWindow countBound = new(
            maximumPendingDeliveries: 1,
            maximumPendingBytes: 16);
        countBound.Add(CreateFrame(1, payloadLength: 1), "StatePacket", now: 0);
        Assert.Throws<InvalidOperationException>(() => countBound.Add(
            CreateFrame(2, payloadLength: 1),
            "StatePacket",
            now: 0));

        ApplicationReliabilityPendingWindow byteBound = new(
            maximumPendingDeliveries: 4,
            maximumPendingBytes: 3);
        byteBound.Add(CreateFrame(3, payloadLength: 2), "StatePacket", now: 0);
        Assert.Throws<InvalidOperationException>(() => byteBound.Add(
            CreateFrame(4, payloadLength: 2),
            "StatePacket",
            now: 0));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ReceiveWindowBuffersAndReleasesDeliveriesInSequence() {
        ApplicationReliabilityReceiveWindow window = new();
        ApplicationReliabilityReceiveResult third = window.Accept(
            CreateFrame(3, payloadLength: 1),
            new StatePacket());
        Assert.Equal(ApplicationReliabilityReceiveDisposition.Buffered, third.Disposition);
        Assert.Empty(third.Ready);
        Assert.Equal(1, window.Count);

        ApplicationReliabilityReceiveResult first = window.Accept(
            CreateFrame(1, payloadLength: 1),
            new StatePacket());
        Assert.Equal(ApplicationReliabilityReceiveDisposition.Ready, first.Disposition);
        Assert.Equal([1L], first.Ready.Select(item => item.Frame.DeliveryId));

        ApplicationReliabilityReceiveResult second = window.Accept(
            CreateFrame(2, payloadLength: 1),
            new StatePacket());
        Assert.Equal(ApplicationReliabilityReceiveDisposition.Ready, second.Disposition);
        Assert.Equal([2L, 3L], second.Ready.Select(item => item.Frame.DeliveryId));
        Assert.Equal(0, window.Count);
        Assert.Equal(4, window.NextDeliveryId);

        Assert.Equal(
            ApplicationReliabilityReceiveDisposition.DuplicateDelivered,
            window.Accept(CreateFrame(2, payloadLength: 1), new StatePacket()).Disposition);
        Assert.Throws<InvalidDataException>(() => window.Accept(
            CreateFrame(
                window.NextDeliveryId
                    + ApplicationReliabilityReceiveWindow.MaximumSequenceGap
                    + 1,
                payloadLength: 1),
            new StatePacket()));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TransportsDoNotRegisterReliableDeliveriesForForeignSessions() {
        ApplicationReliabilityRuntime.Reset();
        ClientSession foreignSession = new() {
            CompatibilityState = NetworkCompatibilityState.Accepted,
            AdmissionState = NetworkSessionAdmissionState.Accepted,
            NetworkState = NetworkState.Playing
        };

        new NetworkServer().Send(
            new StatePacket(3, NetworkState.Playing),
            foreignSession);
        new RelayHost().Send(
            new StatePacket(3, NetworkState.Playing),
            foreignSession);

        Assert.Equal(0, ApplicationReliabilityRuntime.Metrics.Created);
        Assert.Equal(0, ApplicationReliabilityRuntime.Metrics.Pending);
        ApplicationReliabilityRuntime.Reset();
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MalformedInnerFrameFailsClosedForOnlyItsServerSession() {
        PacketManager.Initialize();
        ApplicationReliabilityRuntime.Reset();
        ClientSession session = new() {
            AdmissionState = NetworkSessionAdmissionState.Accepted,
            NetworkState = NetworkState.Playing
        };
        ApplicationReliablePacket malformed = ApplicationReliablePacket.CreateData(
            1,
            NetworkChannels.Default,
            NetworkState.Playing,
            true,
            [PacketFragment.VerifyByte, 254]);
        malformed.From = session;

        ApplicationReliabilityRuntime.Handle(malformed, isServer: true);
        ApplicationReliabilityRuntime.Handle(malformed, isServer: true);

        Assert.Equal(1, ApplicationReliabilityRuntime.Metrics.Failed);
        Assert.Equal(1, ApplicationReliabilityRuntime.PendingFailureCount);
        Assert.Equal(0, ApplicationReliabilityRuntime.Metrics.Pending);
        ApplicationReliabilityRuntime.Reset();
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ThrowingInnerHandlerIsIsolatedAndSchedulesClientClosure() {
        PacketManager.Initialize();
        PacketManager.RegisterPacket<ThrowingPacket>();
        ApplicationReliabilityRuntime.Reset();
        try {
            ThrowingPacket inner = new();
            ApplicationReliablePacket frame = ApplicationReliablePacket.CreateData(
                1,
                inner.Channel,
                inner.MinNeedState,
                inner.RequiresProject,
                PacketManager.EncodeRawPacket(inner));

            ApplicationReliabilityRuntime.Handle(frame, isServer: false);

            Assert.Equal(1, ApplicationReliabilityRuntime.Metrics.Failed);
            Assert.Equal(1, ApplicationReliabilityRuntime.PendingFailureCount);
        }
        finally {
            PacketManager.UnRegisterPacket(ThrowingPacket.PacketId);
            ApplicationReliabilityRuntime.Reset();
        }
    }

    private static ApplicationReliablePacket RoundTrip(
        ApplicationReliablePacket packet) {
        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            packet.Serialize(new PacketSerializer(writer));
            payload = writer.Data();
        }
        ApplicationReliablePacket decoded = new();
        using PacketStreamReader reader = new(payload);
        decoded.Serialize(new PacketSerializer(reader));
        return decoded;
    }

    private static ApplicationReliablePacket CreateFrame(
        long deliveryId,
        int payloadLength) =>
        ApplicationReliablePacket.CreateData(
            deliveryId,
            NetworkChannels.Default,
            NetworkState.Playing,
            true,
            Enumerable.Repeat((byte)0x88, payloadLength).ToArray());

    private sealed class ThrowingPacket : Packet {
        internal const byte PacketId = 254;

        public override byte ID => PacketId;
        public override NetworkState MinNeedState => NetworkState.Playing;

        public override void Handle(bool isServer) =>
            throw new InvalidOperationException("requested handler failure");

        public override void Serialize(PacketSerializer archive) {
        }
    }

    private sealed class ChannelPacket(byte channel) : Packet {
        public override byte ID => 253;
        public override byte Channel => channel;

        public override void Handle(bool isServer) {
        }

        public override void Serialize(PacketSerializer archive) {
        }
    }
}
