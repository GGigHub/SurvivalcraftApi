using Engine;

namespace Game.NetWork;

public class TerrainChangeCellPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainChangeCell;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_X;
    public byte m_Y;
    public int m_Z;
    public int m_value;

    public TerrainChangeCellPackage() { }

    public TerrainChangeCellPackage(int x, int y, int z, int value) {
        m_X = x;
        m_Y = (byte)y;
        m_Z = z;
        m_value = value;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_X);
        archive.Value(ref m_Y);
        archive.Value(ref m_Z);
        archive.Value(ref m_value);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        int chunkX = m_X >> 4;
        int chunkZ = m_Z >> 4;
        TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
        if (chunk != null) {
            subsystemTerrain.ChangeCellNet(m_X, m_Y, m_Z, m_value);
        }
    }
}