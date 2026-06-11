using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerUpdateLocationPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerUpdateLocation;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public TerrainUpdater.UpdateLocation m_updateLocation;

    public PlayerUpdateLocationPacket() { }

    public PlayerUpdateLocationPacket(TerrainUpdater.UpdateLocation updateLocation) {
        m_updateLocation = updateLocation;
    }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_updateLocation = new TerrainUpdater.UpdateLocation();
        }
        Vector2 center = m_updateLocation.Center;
        ushort contentDistance = (ushort)m_updateLocation.ContentDistance;
        ushort visibilityDistance = (ushort)m_updateLocation.VisibilityDistance;
        Vector2? lastChunksUpdateCenter = m_updateLocation.LastChunksUpdateCenter;

        archive.Value(ref center);
        archive.Value(ref contentDistance);
        archive.Value(ref visibilityDistance);
        archive.Value(ref lastChunksUpdateCenter);

        m_updateLocation.Center = center;
        m_updateLocation.ContentDistance = contentDistance;
        m_updateLocation.VisibilityDistance = visibilityDistance;
        m_updateLocation.LastChunksUpdateCenter = lastChunksUpdateCenter;
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var player = PlayerPacketBase.ResolvePlayer(From?.PlayerIndex ?? -1);
        if (player == null) return;
        var updater = GameManager.Project.FindSubsystem<SubsystemTerrain>().TerrainUpdater;
        updater.SetLastChunksUpdateCenter(player.PlayerIndex, m_updateLocation.LastChunksUpdateCenter);
        updater.SetUpdateLocation(player.PlayerIndex, m_updateLocation.Center, m_updateLocation.VisibilityDistance, m_updateLocation.ContentDistance);
    }
}
