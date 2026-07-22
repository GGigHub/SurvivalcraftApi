using Engine;

namespace Game.Network;

public class TerrainViewDistancePacket : Packet {
    public const float MaximumDistance = 1024f;

    public override byte ID => (byte)PacketType.TerrainViewDistance;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public Vector2 Center;
    public float VisibilityDistance;
    public float ContentDistance;

    public TerrainViewDistancePacket() { }

    public TerrainViewDistancePacket(TerrainUpdater.UpdateLocation location) {
        Center = location.Center;
        VisibilityDistance = location.VisibilityDistance;
        ContentDistance = location.ContentDistance;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref Center);
        archive.Value(ref VisibilityDistance);
        archive.Value(ref ContentDistance);
    }

    public override void Handle(bool isServer) {
        if (!isServer || From == null || From.PlayerIndex < 0) {
            return;
        }

        float visibilityDistance = Math.Clamp(VisibilityDistance, 0f, MaximumDistance);
        float contentDistance = Math.Clamp(ContentDistance, 0f, MaximumDistance);
        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        TerrainUpdater updater = terrain?.TerrainUpdater;
        updater?.SetUpdateLocation(From.PlayerIndex, Center, visibilityDistance, contentDistance);
        updater?.SetPlayerChunkSyncLocation(From.PlayerIndex, Center, contentDistance);
    }
}
