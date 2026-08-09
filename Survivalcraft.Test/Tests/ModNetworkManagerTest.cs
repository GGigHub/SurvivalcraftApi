using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModNetworkManagerCollection {
    public const string Name = "ModNetworkManager";
}

[Collection(ModNetworkManagerCollection.Name)]
public sealed class ModNetworkManagerTest : IDisposable {
    public ModNetworkManagerTest() {
        ModNetworkManager.ResetRegistrations();
    }

    public void Dispose() {
        ModNetworkManager.ResetRegistrations();
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RegistrationCanonicalizesIdentityAndHandleDisposeUnregisters() {
        TestLoader loader = CreateLoader("Example.Mod");

        ModNetworkChannel channel = loader.RegisterNetworkChannel(
            " Echo ",
            2,
            ModNetworkDirection.Bidirectional,
            (_, _) => { },
            maximumPayloadBytes: 512);

        ModNetworkChannelDescriptor descriptor = Assert.Single(
            ModNetworkManager.GetLocalCatalog().Channels);
        Assert.Equal("example.mod:echo", descriptor.QualifiedName);
        Assert.True(channel.IsRegistered);

        channel.Dispose();

        Assert.False(channel.IsRegistered);
        Assert.Empty(ModNetworkManager.GetLocalCatalog().Channels);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void RegistrationIsRejectedAfterMultiplayerTransportStarts() {
        TestLoader loader = CreateLoader("late.mod");
        Func<bool>? previousIsClientRunning = NetworkManager.IsClientRunningFunc;
        NetworkManager.NetworkClient = new NetworkClient { IsConnected = true };
        NetworkManager.IsClientRunningFunc = () => true;
        try {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                loader.RegisterNetworkChannel(
                    "late",
                    1,
                    ModNetworkDirection.Bidirectional,
                    (_, _) => { }));

            Assert.Contains("before starting", exception.Message, StringComparison.Ordinal);
            Assert.Empty(ModNetworkManager.GetLocalCatalog().Channels);
        }
        finally {
            NetworkManager.NetworkClient = null;
            NetworkManager.IsClientRunningFunc = previousIsClientRunning;
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LocalOnlyModCannotRegisterNetworkChannels() {
        TestLoader loader = CreateLoader("local.mod");
        loader.Entity.modInfo.NetworkScope = ModNetworkScope.LocalOnly;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            loader.RegisterNetworkChannel(
                "local",
                1,
                ModNetworkDirection.Bidirectional,
                (_, _) => { }));

        Assert.Contains("Local-only", exception.Message, StringComparison.Ordinal);
        Assert.Empty(ModNetworkManager.GetLocalCatalog().Channels);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ModInfoNetworkScopeDefaultsRequiredAndParsesNamedValuesSafely() {
        ModInfo defaultInfo = ModsManager.DeserializeJson(
            "{\"PackageName\":\"default.mod\"}");
        ModInfo optionalInfo = ModsManager.DeserializeJson(
            "{\"PackageName\":\"optional.mod\",\"NetworkScope\":\"optional\"}");
        ModInfo invalidInfo = ModsManager.DeserializeJson(
            "{\"PackageName\":\"invalid.mod\",\"NetworkScope\":\"1\"}");

        Assert.Equal(ModNetworkScope.Required, defaultInfo.NetworkScope);
        Assert.Equal(ModNetworkScope.Optional, optionalInfo.NetworkScope);
        Assert.Equal(ModNetworkScope.Required, invalidInfo.NetworkScope);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CompatibilityHelloCannotRestartAnAcceptedSession() {
        NetworkCompatibilityResult accepted = NetworkCompatibilityResult.Accept();
        ClientSession session = new() {
            CompatibilityState = NetworkCompatibilityState.Accepted,
            CompatibilityResult = accepted
        };
        CompatibilityHelloPacket replayedHello = new() {
            From = session
        };

        replayedHello.Handle(isServer: true);

        Assert.Equal(NetworkCompatibilityState.Accepted, session.CompatibilityState);
        Assert.Same(accepted, session.CompatibilityResult);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void ChannelResultPacketRoundTripsNegotiatedCatalog() {
        ModChannelCatalogResultPacket source = new(
            NetworkCompatibilityResult.Accept(),
            new ModNetworkChannelCatalog([
                new ModNetworkChannelDescriptor(
                    "example.mod",
                    "state",
                    2,
                    ModNetworkDirection.Bidirectional,
                    512)
            ]));

        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            source.Serialize(new PacketSerializer(writer));
            payload = writer.Data();
        }
        ModChannelCatalogResultPacket decoded = new();
        using PacketStreamReader reader = new(payload);
        decoded.Serialize(new PacketSerializer(reader));

        Assert.True(decoded.Result.Accepted);
        ModNetworkChannelDescriptor channel = Assert.Single(decoded.Catalog.Channels);
        Assert.Equal("example.mod:state", channel.QualifiedName);
        Assert.Equal(2, channel.Version);
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void PerPackageLimitCannotBeBypassedWithMultipleLoaders() {
        TestLoader first = CreateLoader("shared.mod");
        TestLoader second = CreateLoader("shared.mod");
        int split = ModNetworkProtocol.MaximumChannelsPerPackage / 2;
        for (int index = 0; index < split; index++) {
            _ = first.RegisterNetworkChannel(
                $"first{index}",
                1,
                ModNetworkDirection.Bidirectional,
                (_, _) => { });
        }
        for (int index = split; index < ModNetworkProtocol.MaximumChannelsPerPackage; index++) {
            _ = second.RegisterNetworkChannel(
                $"second{index}",
                1,
                ModNetworkDirection.Bidirectional,
                (_, _) => { });
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            second.RegisterNetworkChannel(
                "overflow",
                1,
                ModNetworkDirection.Bidirectional,
                (_, _) => { }));

        Assert.Contains(
            ModNetworkProtocol.MaximumChannelsPerPackage.ToString(),
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            ModNetworkProtocol.MaximumChannelsPerPackage,
            ModNetworkManager.GetLocalCatalog().Channels.Count);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void GlobalChannelLimitIsRejectedDuringRegistration() {
        for (int index = 0; index < ModNetworkProtocol.MaximumChannelCount; index++) {
            TestLoader loader = CreateLoader($"mod{index}");
            _ = loader.RegisterNetworkChannel(
                "channel",
                1,
                ModNetworkDirection.Bidirectional,
                (_, _) => { });
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            CreateLoader("overflow.mod").RegisterNetworkChannel(
                "channel",
                1,
                ModNetworkDirection.Bidirectional,
                (_, _) => { }));

        Assert.Contains(
            ModNetworkProtocol.MaximumChannelCount.ToString(),
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            ModNetworkProtocol.MaximumChannelCount,
            ModNetworkManager.GetLocalCatalog().Channels.Count);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void ModEntityDisposeCleansEveryOwnerEvenWhenOneLoaderThrows() {
        ModEntity entity = CreateEntity("lifecycle.mod");
        TestLoader throwing = new(throwOnDispose: true) { Entity = entity };
        TestLoader healthy = new() { Entity = entity };
        entity.Loaders.Add(throwing);
        entity.Loaders.Add(healthy);
        ModNetworkChannel first = throwing.RegisterNetworkChannel(
            "first",
            1,
            ModNetworkDirection.Bidirectional,
            (_, _) => { });
        ModNetworkChannel second = healthy.RegisterNetworkChannel(
            "second",
            1,
            ModNetworkDirection.Bidirectional,
            (_, _) => { });

        Exception? exception = Record.Exception(entity.Dispose);

        Assert.Null(exception);
        Assert.True(healthy.DisposeCalled);
        Assert.False(first.IsRegistered);
        Assert.False(second.IsRegistered);
        Assert.Empty(ModNetworkManager.GetLocalCatalog().Channels);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void DispatchRejectsMessageWhenProjectIsUnavailableWithoutCallingHandler() {
        bool called = false;
        TestLoader loader = CreateLoader("dispatch.mod");
        _ = loader.RegisterNetworkChannel(
            "input",
            1,
            ModNetworkDirection.ClientToServer,
            (_, _) => called = true,
            maximumPayloadBytes: 32);
        ModNetworkEnvelope envelope = new(
            Guid.NewGuid(),
            "dispatch.mod",
            "input",
            1,
            [1, 2, 3]);

        ClientSession session = new();
        NegotiateLocalCatalog(session);
        ModNetworkDispatchCode result = ModNetworkManager.Dispatch(
            envelope,
            isServer: true,
            session);

        Assert.Equal(ModNetworkDispatchCode.ProjectUnavailable, result);
        Assert.False(called);
        Assert.Equal(1, ModNetworkManager.Metrics.Dropped);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void DispatchEnforcesDirectionAndPerChannelPayloadBeforeHandler() {
        bool called = false;
        TestLoader loader = CreateLoader("guard.mod");
        _ = loader.RegisterNetworkChannel(
            "output",
            1,
            ModNetworkDirection.ServerToClient,
            (_, _) => called = true,
            maximumPayloadBytes: 8);
        _ = loader.RegisterNetworkChannel(
            "input",
            1,
            ModNetworkDirection.ClientToServer,
            (_, _) => called = true,
            maximumPayloadBytes: 1);
        ClientSession session = new();
        NegotiateLocalCatalog(session);

        ModNetworkDispatchCode direction = ModNetworkManager.Dispatch(
            new ModNetworkEnvelope(
                Guid.NewGuid(),
                "guard.mod",
                "output",
                1,
                [1]),
            isServer: true,
            session);
        ModNetworkDispatchCode payload = ModNetworkManager.Dispatch(
            new ModNetworkEnvelope(
                Guid.NewGuid(),
                "guard.mod",
                "input",
                1,
                [1, 2]),
            isServer: true,
            session);

        Assert.Equal(ModNetworkDispatchCode.DirectionDenied, direction);
        Assert.Equal(ModNetworkDispatchCode.PayloadTooLarge, payload);
        Assert.False(called);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void ProjectAndSessionCleanupResetPerPeerDeduplicationState() {
        TestLoader loader = CreateLoader("cleanup.mod");
        _ = loader.RegisterNetworkChannel(
            "input",
            1,
            ModNetworkDirection.ClientToServer,
            (_, _) => { });
        ClientSession session = new();
        NegotiateLocalCatalog(session);
        ModNetworkEnvelope envelope = new(
            Guid.NewGuid(),
            "cleanup.mod",
            "input",
            1,
            [1]);

        Assert.Equal(
            ModNetworkDispatchCode.ProjectUnavailable,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));
        Assert.Equal(
            ModNetworkDispatchCode.Duplicate,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));

        ModNetworkManager.OnProjectDisposed();
        Assert.Equal(
            ModNetworkDispatchCode.ProjectUnavailable,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));
        Assert.Equal(
            ModNetworkDispatchCode.Duplicate,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));

        ModNetworkManager.ClearSession(session);
        Assert.Equal(
            ModNetworkDispatchCode.UnnegotiatedChannel,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));
        NegotiateLocalCatalog(session);
        Assert.Equal(
            ModNetworkDispatchCode.Duplicate,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));
        Assert.Equal(
            ModNetworkDispatchCode.ProjectUnavailable,
            ModNetworkManager.Dispatch(
                new ModNetworkEnvelope(
                    Guid.NewGuid(),
                    "cleanup.mod",
                    "input",
                    1,
                    [1]),
                isServer: true,
                session));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void DispatchRejectsRegisteredButUnnegotiatedChannelBeforeHandler() {
        bool called = false;
        TestLoader loader = CreateLoader("isolated.mod");
        _ = loader.RegisterNetworkChannel(
            "input",
            1,
            ModNetworkDirection.ClientToServer,
            (_, _) => called = true);

        ModNetworkDispatchCode result = ModNetworkManager.Dispatch(
            new ModNetworkEnvelope(
                Guid.NewGuid(),
                "isolated.mod",
                "input",
                1,
                [1]),
            isServer: true,
            new ClientSession());

        Assert.Equal(ModNetworkDispatchCode.UnnegotiatedChannel, result);
        Assert.False(called);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void UnnegotiatedTrafficStillConsumesPerPeerRateLimit() {
        TestLoader loader = CreateLoader("bounded.mod");
        _ = loader.RegisterNetworkChannel(
            "input",
            1,
            ModNetworkDirection.ClientToServer,
            (_, _) => { });
        ClientSession session = new();

        for (int index = 0;
             index < ModNetworkTrafficLimiter.DefaultMaximumMessagesPerWindow;
             index++) {
            Assert.Equal(
                ModNetworkDispatchCode.UnnegotiatedChannel,
                ModNetworkManager.Dispatch(
                    new ModNetworkEnvelope(
                        Guid.NewGuid(),
                        "bounded.mod",
                        "input",
                        1,
                        [1]),
                    isServer: true,
                    session));
        }
        Assert.Equal(
            ModNetworkDispatchCode.RateLimited,
            ModNetworkManager.Dispatch(
                new ModNetworkEnvelope(
                    Guid.NewGuid(),
                    "bounded.mod",
                    "input",
                    1,
                    [1]),
                isServer: true,
                session));
        Assert.Equal(1, ModNetworkManager.Metrics.RateLimited);
    }

    private static void NegotiateLocalCatalog(ClientSession session) =>
        ModNetworkManager.SetNegotiatedCatalog(
            session,
            ModNetworkManager.GetLocalCatalog());

    private static TestLoader CreateLoader(string packageName) =>
        new() { Entity = CreateEntity(packageName) };

    private static ModEntity CreateEntity(string packageName) =>
        new() {
            modInfo = new ModInfo {
                Name = packageName,
                PackageName = packageName,
                Version = "1.0.0",
                ApiVersion = "1.9.3"
            }
        };

    private sealed class TestLoader(bool throwOnDispose = false) : ModLoader {
        public bool DisposeCalled { get; private set; }

        public override void ModDispose() {
            DisposeCalled = true;
            if (throwOnDispose) {
                throw new InvalidOperationException("Intentional test disposal failure.");
            }
        }
    }
}
