using Engine;

namespace Game.Network;

public class ExplosionPacket : Packet {
    public override byte ID => (byte)PacketType.Explosion;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public Dictionary<Point2, List<(Point3, float)>> m_cells;
    public Vector3 m_position;
    public float m_pressure;
    public bool m_playExplosionSound;

    public ExplosionPacket() {
    }

    public ExplosionPacket(
        Dictionary<Point2, List<(Point3, float)>> cells,
        Vector3 position,
        float pressure,
        bool playExplosionSound
    ) {
        m_cells = new Dictionary<Point2, List<(Point3, float)>>();
        foreach (var cell in cells) {
            m_cells.Add(cell.Key, new List<(Point3, float)>(cell.Value));
        }
        m_position = position;
        m_pressure = pressure;
        m_playExplosionSound = playExplosionSound;
    }

    public override void Serialize(PacketSerializer archive) {
        int count = archive.IsWriting ? m_cells.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_cells = [];
            for (int i = 0; i < count; i++) {
                Point2 point = default;
                archive.Value(ref point);
                int cellCount = 0;
                archive.Value(ref cellCount);
                List<(Point3, float)> cells = [];
                for (int j = 0; j < cellCount; j++) {
                    Point3 cell = default;
                    float strength = 0f;
                    archive.Value(ref cell);
                    archive.Value(ref strength);
                    cells.Add((cell, strength));
                }
                m_cells.Add(point, cells);
            }
        }
        else {
            foreach (var chunk in m_cells) {
                Point2 point = chunk.Key;
                archive.Value(ref point);
                int cellCount = chunk.Value.Count;
                archive.Value(ref cellCount);
                foreach (var item in chunk.Value) {
                    Point3 cell = item.Item1;
                    float strength = item.Item2;
                    archive.Value(ref cell);
                    archive.Value(ref strength);
                }
            }
        }
        archive.Value(ref m_position);
        archive.Value(ref m_pressure);
        archive.Value(ref m_playExplosionSound);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        SubsystemExplosions subsystem = GameManager.Project.FindSubsystem<SubsystemExplosions>();
        if (subsystem == null) return;

        foreach (var chunk in m_cells) {
            foreach (var item in chunk.Value) {
                subsystem.m_explosionParticleSystem.SetExplosionCell(item.Item1, item.Item2);
            }
        }

        subsystem.PlayExplosionSound(m_position, m_pressure, m_playExplosionSound);
    }
}
