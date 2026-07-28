using System.Text.Json;
using Survivalcraft.Testing.Control;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ControlProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public async Task LengthPrefixedJsonRoundTripsFragmentSafeFrames() {
        ControlRequest expected = ControlProtocol.CreateRequest(
            ControlCommandNames.Ping,
            new PingCommand("hello"),
            "request-1");
        await using MemoryStream stream = new();

        await LengthPrefixedJsonStream.WriteAsync(stream, expected);
        stream.Position = 0;
        ControlRequest actual = await LengthPrefixedJsonStream.ReadAsync<ControlRequest>(stream)
            ?? throw new InvalidOperationException("Frame was not read.");

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Command, actual.Command);
        Assert.Equal("hello", actual.DeserializePayload<PingCommand>().Message);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task NamedPipeRoundTripCorrelatesTypedResponse() {
        string pipeName = "sc-testing-" + Guid.NewGuid().ToString("N");
        await using NamedPipeControlServer server = new(
            pipeName,
            (request, _) => ValueTask.FromResult(
                ControlProtocol.Success(
                    request,
                    new PingResult(
                        request.DeserializePayload<PingCommand>().Message ?? string.Empty,
                        "unit",
                        Environment.ProcessId,
                        ControlProtocol.CurrentVersion,
                        DateTimeOffset.UnixEpoch),
                    7)));
        await server.StartAsync();
        await using NamedPipeControlClient client = new(pipeName);
        await client.ConnectAsync(TimeSpan.FromSeconds(5));

        PingResult result = await client.SendAsync<PingCommand, PingResult>(
            ControlCommandNames.Ping,
            new PingCommand("correlated"));

        Assert.Equal("correlated", result.Message);
        Assert.Equal("unit", result.Role);
        Assert.Equal(ControlProtocol.CurrentVersion, result.ProtocolVersion);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ErrorResponseBecomesStructuredException() {
        ControlRequest request = ControlProtocol.CreateRequest(
            ControlCommandNames.Ping,
            new PingCommand(),
            "request-2");
        ControlResponse response = ControlProtocol.Failure(
            request,
            "not_ready",
            "Node is still loading.",
            "screen=LoadingScreen");

        ControlCommandException exception = Assert.Throws<ControlCommandException>(
            response.DeserializeResult<ControlAck>);

        Assert.Equal("not_ready", exception.Code);
        Assert.Contains("screen=LoadingScreen", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task InvalidFrameLengthIsRejectedBeforeAllocation() {
        await using MemoryStream stream = new();
        byte[] invalidHeader = BitConverter.GetBytes(ControlProtocol.MaximumFrameBytes + 1);
        await stream.WriteAsync(invalidHeader);
        stream.Position = 0;

        ControlProtocolException exception = await Assert.ThrowsAsync<ControlProtocolException>(
            async () => await LengthPrefixedJsonStream.ReadAsync<JsonDocument>(stream));

        Assert.Equal("invalid_frame_length", exception.Code);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RelayCommandsAndStatusRoundTripAsTypedPayloads() {
        ControlRequest request = ControlProtocol.CreateRequest(
            ControlCommandNames.ConnectRelay,
            new ConnectRelayCommand("127.0.0.1", 28888, "A1B2"));

        ConnectRelayCommand command = request.DeserializePayload<ConnectRelayCommand>();
        Assert.Equal("127.0.0.1", command.Host);
        Assert.Equal(28888, command.Port);
        Assert.Equal("A1B2", command.RoomCode);

        RelayStatus expected = new(
            "host",
            HostConnected: true,
            ClientConnected: false,
            "Ready",
            "A1B2",
            string.Empty,
            HostSessionCount: 1);
        RelayStatus actual = ControlProtocol.Success(request, expected)
            .DeserializeResult<RelayStatus>();
        Assert.Equal(expected, actual);
    }
}
