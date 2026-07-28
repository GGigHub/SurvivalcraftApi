using System.Text.Json;
using Game;
using Game.Network;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const string TestChannelName = "automation";
    private const int TestChannelVersion = 1;
    private const int TestChannelMaximumPayloadBytes = 4096;
    private const int MaximumRetainedTestMessages = 256;

    private sealed record TestModWireMessage(
        string CorrelationId,
        string Payload,
        bool EchoRequested,
        bool HandlerFailureRequested);

    private readonly object testModMessageSync = new();
    private readonly List<TestModMessageState> testModMessages = [];
    private ModNetworkChannel? testModChannel;

    private void InitializeTestModChannel() {
        testModChannel = RegisterNetworkChannel(
            TestChannelName,
            TestChannelVersion,
            ModNetworkDirection.Bidirectional,
            HandleTestModMessage,
            TestChannelMaximumPayloadBytes);
        ResetTestModMessages();
    }

    private void DisposeTestModChannel() {
        testModChannel?.Dispose();
        testModChannel = null;
    }

    private void HandleTestModMessage(
        ModNetworkMessageContext context,
        ReadOnlyMemory<byte> payload) {
        TestModWireMessage message = JsonSerializer.Deserialize<TestModWireMessage>(
            payload.Span,
            ControlProtocol.JsonOptions)
            ?? throw new InvalidDataException("Test mod message payload is empty.");
        TestModMessageState state = new(
            DateTimeOffset.UtcNow,
            message.CorrelationId ?? string.Empty,
            message.Payload ?? string.Empty,
            context.MessageId.ToString("N"),
            context.IsServer,
            context.Sender?.PlayerIndex >= 0 ? context.Sender.PlayerIndex : null,
            message.EchoRequested,
            message.HandlerFailureRequested);
        lock (testModMessageSync) {
            testModMessages.Add(state);
            if (testModMessages.Count > MaximumRetainedTestMessages) {
                testModMessages.RemoveRange(
                    0,
                    testModMessages.Count - MaximumRetainedTestMessages);
            }
        }

        if (message.HandlerFailureRequested) {
            throw new InvalidOperationException(
                $"Requested test handler failure for '{message.CorrelationId}'.");
        }
        if (context.IsServer && message.EchoRequested && context.Sender != null) {
            TestModWireMessage echo = message with { EchoRequested = false };
            testModChannel?.SendToClient(context.Sender, EncodeTestModMessage(echo));
        }
    }

    private TestModSendResult SendTestModMessage(SendTestModMessageCommand command) {
        ModNetworkChannel channel = testModChannel
            ?? throw new InvalidOperationException("The test mod channel is not registered.");
        string correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : command.CorrelationId.Trim();
        TestModWireMessage message = new(
            correlationId,
            command.Payload ?? string.Empty,
            EchoRequested: NetworkManager.IsClientRunning,
            command.FailHandler);
        byte[] payload = EncodeTestModMessage(message);

        if (NetworkManager.IsClientRunning) {
            if (command.Broadcast) {
                throw new InvalidOperationException(
                    "A client test message cannot request a server broadcast.");
            }
            channel.SendToServer(payload);
            return new TestModSendResult(
                correlationId,
                "client-to-server",
                1,
                payload.Length);
        }
        if (!NetworkManager.IsServerRunning) {
            throw new InvalidOperationException("No multiplayer connection is active.");
        }
        if (command.Broadcast) {
            int recipients = channel.Broadcast(payload);
            return new TestModSendResult(
                correlationId,
                "server-broadcast",
                recipients,
                payload.Length);
        }

        ClientSession? recipient = NetworkManager.ServerSessions.FirstOrDefault(session =>
            session.CompatibilityState == NetworkCompatibilityState.Accepted
            && session.NetworkState >= NetworkState.ProjectLoaded);
        if (recipient == null) {
            throw new InvalidOperationException("No active client session can receive the test message.");
        }
        channel.SendToClient(recipient, payload);
        return new TestModSendResult(
            correlationId,
            "server-to-client",
            1,
            payload.Length);
    }

    private ModChannelStatus ConfigureModChannels(ConfigureModChannelsCommand command) {
        if (command.ClearOverride) {
            ModNetworkManager.ClearLocalCatalogOverride();
            return BuildModChannelStatus();
        }
        ModNetworkManager.SetLocalCatalogOverride(new ModNetworkChannelCatalog(
            (command.Channels ?? []).Select(channel => new ModNetworkChannelDescriptor(
                channel.PackageName,
                channel.ChannelName,
                channel.Version,
                (ModNetworkDirection)channel.Directions,
                channel.MaximumPayloadBytes))));
        return BuildModChannelStatus();
    }

    private ModChannelStatus BuildModChannelStatus() {
        ModNetworkChannelCatalog catalog = ModNetworkManager.GetLocalCatalog();
        ModNetworkRuntimeMetrics metrics = ModNetworkManager.Metrics;
        ModNetworkDispatchRecord? dispatch = ModNetworkManager.LastDispatch;
        return new ModChannelStatus(
            ModNetworkManager.HasCatalogOverride,
            ModNetworkProtocol.ComputeCatalogFingerprint(catalog),
            catalog.Channels.Select(channel => new ModChannelDescriptorState(
                channel.PackageName,
                channel.ChannelName,
                channel.Version,
                (int)channel.Directions,
                channel.MaximumPayloadBytes)).ToArray(),
            new ModNetworkMetricsState(
                metrics.Sent,
                metrics.Received,
                metrics.Delivered,
                metrics.Dropped,
                metrics.Duplicates,
                metrics.RateLimited,
                metrics.HandlerErrors),
            dispatch == null
                ? null
                : new ModNetworkDispatchState(
                    dispatch.Timestamp,
                    dispatch.PackageName,
                    dispatch.ChannelName,
                    dispatch.MessageId.ToString("N"),
                    dispatch.Code.ToString(),
                    dispatch.IsServer,
                    dispatch.PayloadBytes,
                    dispatch.Detail));
    }

    private TestModMessagesStatus BuildTestModMessagesStatus() {
        TestModMessageState[] messages;
        lock (testModMessageSync) {
            messages = testModMessages.ToArray();
        }
        return new TestModMessagesStatus(messages, BuildModChannelStatus());
    }

    private TestModMessagesStatus ResetTestModMessages() {
        lock (testModMessageSync) {
            testModMessages.Clear();
        }
        ModNetworkManager.ResetMetrics();
        return BuildTestModMessagesStatus();
    }

    private static byte[] EncodeTestModMessage(TestModWireMessage message) {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            ControlProtocol.JsonOptions);
        if (payload.Length > TestChannelMaximumPayloadBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                payload.Length,
                $"Encoded test mod message exceeds {TestChannelMaximumPayloadBytes} bytes.");
        }
        return payload;
    }
}
