using System.Collections.Generic;
using Engine;

namespace Game.Network;

public static class NetworkTerrain {
    public static void OnTerrainChangeCell(SubsystemTerrain subsystem, int x, int y, int z, int value) {
        if (!NetworkService.IsClient) {
            NetworkService.QueuePacket(new TerrainChangeCellPacket(x, y, z, value));
        }
    }

    public static void OnTerrainRequestSyncChunks(TerrainUpdater updater, List<Point2> chunks) {
        NetworkService.QueuePacket(new TerrainRequestSyncChunksPacket(chunks));
    }

    public static void OnTerrainSyncChunkList(TerrainUpdater updater, ComponentPlayer player, List<TerrainChunk> chunks) {
        NetworkService.QueuePacket(new TerrainSyncChunkListPacket(chunks) { To = NetworkService.GetClientByGUID(player?.PlayerData?.NetworkGuid) });
    }

    public static void OnTerrainReplyResult(TerrainUpdater updater, ComponentPlayer player, List<Point2> chunks) {
        NetworkService.QueuePacket(new TerrainReplyResultPacket(chunks) { To = NetworkService.GetClientByGUID(player?.PlayerData?.NetworkGuid) });
    }

    public static void OnTerrainPlayerLocationUpdate(TerrainUpdater updater, TerrainUpdater.UpdateLocation location) {
        NetworkService.QueuePacket(new PlayerUpdateLocationPacket(location));
    }
}

