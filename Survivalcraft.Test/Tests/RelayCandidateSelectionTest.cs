using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class RelayCandidateSelectionTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void RankingExcludesAttemptedEndpointInsteadOfReusingDynamicRank() {
        RelayCandidateInfo failed = Candidate(
            "failed-now-rank-two",
            "103.40.14.192",
            lastSeenSecondsAgo: 8);
        RelayCandidateInfo healthy = Candidate(
            "healthy-now-rank-one",
            "43.240.193.232",
            lastSeenSecondsAgo: 1);
        string attempted = CenterServerClient.GetRelayEndpointKey(
            failed.Host,
            failed.UdpPort);

        RelayCandidateInfo selected = Assert.Single(
            CenterServerClient.RankRelayCandidates([failed, healthy], [attempted]));

        Assert.Same(healthy, selected);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RankingDeduplicatesEndpointAfterApplyingStablePriorityOrder() {
        RelayCandidateInfo lowerPriorityDuplicate = Candidate(
            "duplicate-low",
            "relay.example",
            priority: 2,
            lastSeenSecondsAgo: 0);
        RelayCandidateInfo preferredDuplicate = Candidate(
            "duplicate-preferred",
            "RELAY.EXAMPLE",
            priority: 1,
            lastSeenSecondsAgo: 10);
        RelayCandidateInfo other = Candidate(
            "other",
            "other.example",
            priority: 3,
            lastSeenSecondsAgo: 0);

        RelayCandidateInfo[] ranked = CenterServerClient.RankRelayCandidates([
            lowerPriorityDuplicate,
            other,
            preferredDuplicate
        ]);

        Assert.Equal(2, ranked.Length);
        Assert.Same(preferredDuplicate, ranked[0]);
        Assert.Same(other, ranked[1]);
    }

    private static RelayCandidateInfo Candidate(
        string relayId,
        string host,
        int priority = 0,
        int lastSeenSecondsAgo = 0) => new() {
        RelayId = relayId,
        Host = host,
        UdpPort = 28888,
        Priority = priority,
        LastSeenSecondsAgo = lastSeenSecondsAgo,
        Status = "Active"
    };
}
