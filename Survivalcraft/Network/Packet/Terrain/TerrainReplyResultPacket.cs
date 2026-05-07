using Engine;

namespace Game.Network;

public class TerrainReplyResultPacket : Packet {
    public override byte ID => (byte)PacketType.TerrainReplyResult;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Point2> RelateChunks = [];

    public TerrainReplyResultPacket() { }

    public TerrainReplyResultPacket(List<Point2> resultPoints) {
        RelateChunks = [.. resultPoints];
    }

    public override void Serialize(PacketSerializer archive) {
        TerrainHelpers.SerializePoint2List(archive, ref RelateChunks, useUShort: true);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (Point2 point in RelateChunks) {
            TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(point.X, point.Y);
            if (chunk != null) {
                chunk.IsRequested = false;
                chunk.WasUpgraded = true;
                chunk.WasDowngraded = true;
            }
        }
    }
}

