using System.Text;
using Survivalcraft.Testing.Determinism;
using Survivalcraft.Testing.Networking;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class DeterminismTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void SchedulerExecutesByDueTimeThenInsertionOrder() {
        ManualClock clock = new();
        DeterministicScheduler scheduler = new(clock);
        List<string> events = [];
        scheduler.Schedule(TimeSpan.FromMilliseconds(20), () => events.Add("third"));
        scheduler.Schedule(TimeSpan.FromMilliseconds(10), () => events.Add("first"));
        scheduler.Schedule(TimeSpan.FromMilliseconds(10), () => events.Add("second"));

        int executed = scheduler.AdvanceTo(20);

        Assert.Equal(3, executed);
        Assert.Equal(["first", "second", "third"], events);
        Assert.Equal(20, clock.Milliseconds);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void SimulatedNetworkProducesIdenticalTraceForSameSeed() {
        NetworkFaultProfile profile = new(
            Seed: 7341,
            BaseLatencyMilliseconds: 20,
            JitterMilliseconds: 5,
            LossRate: 0.2,
            DuplicationRate: 0.25,
            ReorderRate: 0.3,
            ReorderDelayMilliseconds: 30,
            BandwidthBytesPerSecond: 2000);

        SimulationResult first = RunSimulation(profile);
        SimulationResult second = RunSimulation(profile);

        Assert.Equal(first.Deliveries, second.Deliveries);
        Assert.Equal(first.Trace, second.Trace);
        Assert.NotEmpty(first.Deliveries);
        Assert.Contains(first.Trace, entry => entry.Contains("dropped", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void PartitionDropsQueuedPacketsUntilEndpointReconnects() {
        SimulatedNetwork network = new(new NetworkFaultProfile(BaseLatencyMilliseconds: 10));
        List<string> deliveries = [];
        network.Register("server", _ => { });
        network.Register("client", packet => deliveries.Add(Encoding.UTF8.GetString(packet.Payload)));

        network.Send("server", "client", "before"u8);
        network.Disconnect("client");
        network.AdvanceBy(TimeSpan.FromMilliseconds(20));
        network.Reconnect("client");
        network.Send("server", "client", "after"u8);
        network.RunUntilIdle();

        Assert.Equal(["after"], deliveries);
        Assert.Contains(network.Trace, item => item.Kind == "dropped" && item.Reason == "partition");
    }

    private static SimulationResult RunSimulation(NetworkFaultProfile profile) {
        SimulatedNetwork network = new(profile);
        List<string> deliveries = [];
        network.Register("server", _ => { });
        network.Register("client", packet =>
            deliveries.Add($"{packet.Sequence}:{Encoding.UTF8.GetString(packet.Payload)}:{packet.DeliveredAtMilliseconds}"));
        for (int index = 0; index < 20; index++) {
            network.Send(
                "server",
                "client",
                Encoding.UTF8.GetBytes("packet-" + index));
        }
        network.RunUntilIdle();
        string[] trace = network.Trace.Select(item =>
            $"{item.AtMilliseconds}:{item.Kind}:{item.Sequence}:{item.DueAtMilliseconds}:{item.Reason}").ToArray();
        return new SimulationResult(deliveries.ToArray(), trace);
    }

    private sealed record SimulationResult(string[] Deliveries, string[] Trace);
}
