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
        NetworkManager.NetworkClient = new NetworkClient { IsConnected = true };
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
        }
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

        ModNetworkDispatchCode result = ModNetworkManager.Dispatch(
            envelope,
            isServer: true,
            new ClientSession());

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
            ModNetworkDispatchCode.ProjectUnavailable,
            ModNetworkManager.Dispatch(envelope, isServer: true, session));
    }

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
