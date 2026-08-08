using Engine;

namespace Game.Network;

public class TerrainChangeCellPacket : Packet {
    public override byte ID => (byte)PacketType.TerrainChangeCell;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public override byte Channel => NetworkChannels.Terrain;
    public int m_X;
    public byte m_Y;
    public int m_Z;
    public int m_value;
    public long m_revision;

    public TerrainChangeCellPacket() { }

    public TerrainChangeCellPacket(int x, int y, int z, int value, long revision) {
        m_X = x;
        m_Y = (byte)y;
        m_Z = z;
        m_value = value;
        m_revision = revision;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_X);
        archive.Value(ref m_Y);
        archive.Value(ref m_Z);
        archive.Value(ref m_value);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        TerrainPacketBase.ApplyRevisionedCellChange(
            subsystemTerrain,
            m_X,
            m_Y,
            m_Z,
            m_value,
            m_revision);
    }
}

