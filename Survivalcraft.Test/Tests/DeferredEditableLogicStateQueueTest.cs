using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Xunit;

namespace Survivalcraft.Test;

public sealed class DeferredEditableLogicStateQueueTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void NewestStateIsRetriedOnlyWhenItsChunkArrives() {
        using Project project = new();
        NetworkLogicStateKey key = NetworkLogicStateKey.Editable(
            NetworkLogicElementKind.Battery,
            false,
            0,
            0,
            new Point3(33, 70, -17));
        TestPacket older = new(revision: 3, terrainRevision: 8);
        TestPacket newer = new(revision: 4, terrainRevision: 9);
        TestPacket delayedOlder = new(revision: 2, terrainRevision: 10);

        DeferredEditableLogicStateQueue.Enqueue(project, key, older);
        DeferredEditableLogicStateQueue.Enqueue(project, key, newer);
        DeferredEditableLogicStateQueue.Enqueue(project, key, delayedOlder);

        Assert.Equal(1, DeferredEditableLogicStateQueue.GetPendingCount(project));
        DeferredEditableLogicStateQueue.ApplyChunk(project, chunkX: 1, chunkZ: -2);
        Assert.Equal(0, newer.HandleCount);
        Assert.Equal(1, DeferredEditableLogicStateQueue.GetPendingCount(project));

        DeferredEditableLogicStateQueue.ApplyChunk(project, chunkX: 2, chunkZ: -2);
        Assert.Equal(0, older.HandleCount);
        Assert.Equal(1, newer.HandleCount);
        Assert.Equal(0, delayedOlder.HandleCount);
        Assert.Equal(0, DeferredEditableLogicStateQueue.GetPendingCount(project));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13634)]
    [InlineData(13635)]
    public void AuthoritativePistonStateAllowsServerOwnedExtendedBit(int data) {
        Assert.True(PistonDataPacket.IsValidAuthoritativeState(data));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DistinctDeferredTargetsAreHardBounded() {
        using Project project = new();
        for (int index = 0;
             index < DeferredEditableLogicStateQueue.MaximumPendingStates + 17;
             index++) {
            NetworkLogicStateKey key = NetworkLogicStateKey.Editable(
                NetworkLogicElementKind.Battery,
                false,
                0,
                0,
                new Point3(index * 16, 70, 0));
            DeferredEditableLogicStateQueue.Enqueue(
                project,
                key,
                new TestPacket(revision: 1, terrainRevision: 1));
        }

        Assert.Equal(
            DeferredEditableLogicStateQueue.MaximumPendingStates,
            DeferredEditableLogicStateQueue.GetPendingCount(project));
    }

    private sealed class TestPacket : EditableBlockPacketBase {
        public TestPacket(long revision, long terrainRevision) {
            m_packetKind = EditableLogicPacketKind.State;
            m_result = EditableLogicResultCode.Accepted;
            m_revision = revision;
            m_terrainRevision = terrainRevision;
        }

        public int HandleCount { get; private set; }
        public override byte ID => byte.MaxValue;
        public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
        public override void Serialize(PacketSerializer archive) { }
        public override void Handle(bool isServer) => HandleCount++;
    }
}
