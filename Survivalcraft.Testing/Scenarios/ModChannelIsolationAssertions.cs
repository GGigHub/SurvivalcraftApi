using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Processes;

namespace Survivalcraft.Testing.Scenarios;

internal static class ModChannelIsolationAssertions {
    public static async Task VerifyAsync(
        GameNodeProcess server,
        GameNodeProcess client,
        IDictionary<string, string> metrics,
        string metricPrefix,
        int seed,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int expectedBroadcastRecipients = 0,
        string? targetNetworkIdentity = null,
        IReadOnlyList<GameNodeProcess>? expectedRecipients = null) {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(metrics);

        _ = await server.SendAsync<EmptyCommand, TestModMessagesStatus>(
            ControlCommandNames.ResetTestModMessages,
            new EmptyCommand(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await client.SendAsync<EmptyCommand, TestModMessagesStatus>(
            ControlCommandNames.ResetTestModMessages,
            new EmptyCommand(),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        bool blocked = false;
        try {
            _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                ControlCommandNames.SendTestModMessage,
                new SendTestModMessageCommand("must-not-send"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ControlCommandException exception)
            when (exception.Message.Contains("not negotiated", StringComparison.OrdinalIgnoreCase)) {
            blocked = true;
        }
        if (!blocked) {
            throw new InvalidOperationException(
                $"{metricPrefix}: client API did not block an unnegotiated mod channel.");
        }

        TestModSendResult broadcast =
            await server.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                ControlCommandNames.SendTestModMessage,
                new SendTestModMessageCommand(
                    "must-not-broadcast",
                    Broadcast: true),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (broadcast.RecipientCount != expectedBroadcastRecipients) {
            throw new InvalidOperationException(
                $"{metricPrefix}: expected {expectedBroadcastRecipients} negotiated broadcast recipient(s), got {broadcast.RecipientCount}.");
        }
        if (expectedRecipients != null) {
            await Task.WhenAll(expectedRecipients.Select(recipient =>
                WaitForMessagePresentAsync(
                    recipient,
                    broadcast.CorrelationId,
                    timeout,
                    $"{metricPrefix}: a negotiated client did not receive the filtered broadcast.",
                    cancellationToken))).ConfigureAwait(false);
        }
        await AssertMessageRemainsAbsentAsync(
            client,
            broadcast.CorrelationId,
            timeout,
            $"{metricPrefix}: unnegotiated client received a filtered broadcast.",
            cancellationToken).ConfigureAwait(false);

        string correlationPrefix = metricPrefix.Replace('.', '-');
        string clientRaw = $"{correlationPrefix}-client-{seed}";
        _ = await client.SendAsync<InjectTestModEnvelopeCommand, TestModSendResult>(
            ControlCommandNames.InjectTestModEnvelope,
            new InjectTestModEnvelopeCommand("raw-client", clientRaw),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await WaitForTestModStateAsync(
            server,
            status => status.Channel.LastDispatch?.Code == "UnnegotiatedChannel"
                && status.Messages.All(message => message.CorrelationId != clientRaw),
            timeout,
            $"{metricPrefix}: server did not reject a raw unnegotiated client envelope.",
            cancellationToken).ConfigureAwait(false);

        string serverRaw = $"{correlationPrefix}-server-{seed}";
        _ = await server.SendAsync<InjectTestModEnvelopeCommand, TestModSendResult>(
            ControlCommandNames.InjectTestModEnvelope,
            new InjectTestModEnvelopeCommand(
                "raw-server",
                serverRaw,
                targetNetworkIdentity),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await WaitForTestModStateAsync(
            client,
            status => status.Channel.LastDispatch?.Code == "UnnegotiatedChannel"
                && status.Messages.All(message => message.CorrelationId != serverRaw),
            timeout,
            $"{metricPrefix}: client did not reject a raw unnegotiated server envelope.",
            cancellationToken).ConfigureAwait(false);

        metrics[$"{metricPrefix}.outboundBlocked"] = "true";
        metrics[$"{metricPrefix}.rawIngressBlocked"] = "true";
        metrics[$"{metricPrefix}.broadcastRecipients"] =
            expectedBroadcastRecipients.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WaitForMessagePresentAsync(
        GameNodeProcess node,
        string correlationId,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            TestModMessagesStatus status =
                await node.SendAsync<EmptyCommand, TestModMessagesStatus>(
                    ControlCommandNames.GetTestModMessages,
                    new EmptyCommand(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            if (status.Messages.Any(message => message.CorrelationId == correlationId)) {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"{failureMessage} Node={node.Role}.{Environment.NewLine}"
            + node.GetRecentLogText());
    }

    private static async Task AssertMessageRemainsAbsentAsync(
        GameNodeProcess node,
        string correlationId,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
        TimeSpan observationWindow = TimeSpan.FromMilliseconds(
            Math.Min(500, Math.Max(100, timeout.TotalMilliseconds / 20)));
        DateTimeOffset deadline = DateTimeOffset.UtcNow + observationWindow;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            TestModMessagesStatus status =
                await node.SendAsync<EmptyCommand, TestModMessagesStatus>(
                    ControlCommandNames.GetTestModMessages,
                    new EmptyCommand(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            if (status.Messages.Any(message => message.CorrelationId == correlationId)) {
                throw new InvalidOperationException(failureMessage);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<TestModMessagesStatus> WaitForTestModStateAsync(
        GameNodeProcess node,
        Func<TestModMessagesStatus, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        TestModMessagesStatus? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<EmptyCommand, TestModMessagesStatus>(
                ControlCommandNames.GetTestModMessages,
                new EmptyCommand(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (predicate(last)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"{failureMessage} Node={node.Role}; Last={last}.{Environment.NewLine}"
            + node.GetRecentLogText());
    }
}
