using System.Net;
using System.Net.Sockets;
using Survivalcraft.Testing.Processes;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class UdpPortLeaseAllocatorTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void ConcurrentAllocationsAreUniqueBindableAndInsideDedicatedRange() {
        int[] ports = new int[64];

        Parallel.For(0, ports.Length, index => {
            ports[index] = UdpPortLeaseAllocator.Allocate();
        });

        Assert.Equal(ports.Length, ports.Distinct().Count());
        Assert.All(ports, port => Assert.InRange(
            port,
            UdpPortLeaseAllocator.MinimumPort,
            UdpPortLeaseAllocator.MaximumPort));
        Assert.All(ports, port => {
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) {
                ExclusiveAddressUse = true
            };
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
        });
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task GameNodesAndRelayUseOneCooldownLeaseNamespace() {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.Testing.Tests",
            Guid.NewGuid().ToString("N"));
        GameNodeProcess? first = null;
        GameNodeProcess? second = null;
        RelayServerProcess? relay = null;
        try {
            first = new GameNodeProcess(new GameNodeOptions(
                "first",
                AppContext.BaseDirectory,
                Path.Combine(dataRoot, "first")));
            int[] firstPorts = [first.GamePort, first.CommandPort, first.LogPort];
            await first.DisposeAsync();
            first = null;

            second = new GameNodeProcess(new GameNodeOptions(
                "second",
                AppContext.BaseDirectory,
                Path.Combine(dataRoot, "second")));
            relay = new RelayServerProcess(new RelayServerOptions(AppContext.BaseDirectory));
            int[] laterPorts = [second.GamePort, second.CommandPort, second.LogPort, relay.Port];

            Assert.Equal(laterPorts.Length, laterPorts.Distinct().Count());
            Assert.Empty(firstPorts.Intersect(laterPorts));
        }
        finally {
            if (relay != null) {
                await relay.DisposeAsync();
            }
            if (second != null) {
                await second.DisposeAsync();
            }
            if (first != null) {
                await first.DisposeAsync();
            }
        }
    }
}
