using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class ClientPacketDispatcherTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void ProjectPacketsAreDeferredWithinABoundedSessionQueue() {
        ClientPacketDispatcher dispatcher = new();
        ClientSession session = new() {
            NetworkState = NetworkState.Connected,
            AdmissionState = NetworkSessionAdmissionState.Accepted
        };

        for (int index = 0;
             index < ClientPacketDispatcher.MaximumPendingProjectPackets + 1;
             index++) {
            dispatcher.Dispatch(session, new ProjectRequiredPacket());
        }

        Assert.Equal(
            ClientPacketDispatcher.MaximumPendingProjectPackets,
            dispatcher.PendingCount);
        dispatcher.Reset();
        Assert.Equal(0, dispatcher.PendingCount);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ConnectedPlayerCatalogPacketsDeclareTheirProjectDependency() {
        Assert.True(new PlayerAddPacket().RequiresProject);
        Assert.True(new PlayerModifyPacket().RequiresProject);
        Assert.True(new PlayerNoMsgAddPacket().RequiresProject);
        Assert.True(new PlayerNoMsgRemovePacket().RequiresProject);
        Assert.True(new PlayerBuglePacket().RequiresProject);
        Assert.False(new ProjectPacket().RequiresProject);
    }

    sealed class ProjectRequiredPacket : Packet {
        public override byte ID => 254;
        public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

        public override void Handle(bool isServer) {
            throw new InvalidOperationException("Deferred packet must not be handled before project load.");
        }

        public override void Serialize(PacketSerializer archive) { }
    }
}
