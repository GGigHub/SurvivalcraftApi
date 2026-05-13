using System.Collections.Generic;
using Engine;

namespace Game.Network;

public static class NetworkTerrain {
    public static void OnTerrainChangeCell(SubsystemTerrain subsystem, int x, int y, int z, int value) {
        if (!NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new TerrainChangeCellPacket(x, y, z, value));
        }
    }

    public static void OnTerrainRequestSyncChunks(TerrainUpdater updater, List<Point2> chunks) {
        NetworkManager.QueuePacket(new TerrainRequestSyncChunksPacket(chunks));
    }

    public static void OnTerrainSyncChunkList(TerrainUpdater updater, Client toClient, List<TerrainChunk> chunks) {
        NetworkManager.QueuePacket(new TerrainSyncChunkListPacket(chunks) { To = toClient });
    }

    public static void OnTerrainReplyResult(TerrainUpdater updater, Client toClient, List<Point2> chunks) {
        NetworkManager.QueuePacket(new TerrainReplyResultPacket(chunks) { To = toClient });
    }

    public static void OnTerrainPlayerLocationUpdate(TerrainUpdater updater, TerrainUpdater.UpdateLocation location) {
        NetworkManager.QueuePacket(new PlayerUpdateLocationPacket(location));
    }
}

