using Engine;

namespace Game.NetWork.Packages;

public class ExplosionsPackage : IPackage {
    public override byte ID => (byte)PackageType.Explosion;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public enum EventType {
        Sound,
        Cell
    }

    public Vector3 m_position;
    public float m_level;
    public float m_delay;
    public EventType m_type;
    public Dictionary<Point2, List<(Point3, float)>> m_cells;

    public ExplosionsPackage() {
    }

    public ExplosionsPackage(Vector3 position, float level, float delay) {
        m_type = EventType.Sound;
        m_position = position;
        m_level = level;
        m_delay = delay;
    }

    public ExplosionsPackage(Dictionary<Point2, List<(Point3, float)>> cells) {
        m_type = EventType.Cell;
        m_cells = new Dictionary<Point2, List<(Point3, float)>>();
        foreach (var c in cells) {
            m_cells.Add(c.Key, c.Value);
        }
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case EventType.Cell:
                SerializeCells(archive);
                break;
            case EventType.Sound:
                archive.Value(ref m_position);
                archive.Value(ref m_level);
                archive.Value(ref m_delay);
                break;
        }
    }

    private void SerializeCells(PackageSerializer archive) {
        int count = archive.IsWriting ? m_cells.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_cells = [];
            for (int i = 0; i < count; i++) {
                Point2 point = default;
                archive.Value(ref point);
                int cellCount = 0;
                archive.Value(ref cellCount);
                List<(Point3, float)> list = [];
                for (int j = 0; j < cellCount; j++) {
                    Point3 point3 = default;
                    float value = 0f;
                    archive.Value(ref point3);
                    archive.Value(ref value);
                    list.Add((point3, value));
                }
                m_cells.Add(point, list);
            }
        }
        else {
            foreach (var c in m_cells) {
                Point2 point = c.Key;
                archive.Value(ref point);
                int cellCount = c.Value.Count;
                archive.Value(ref cellCount);
                foreach (var item in c.Value) {
                    Point3 point3 = item.Item1;
                    float value = item.Item2;
                    archive.Value(ref point3);
                    archive.Value(ref value);
                }
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        var sub = GameManager.Project.FindSubsystem<SubsystemExplosions>();
        switch (m_type) {
            case EventType.Cell:
                foreach (var i in m_cells) {
                    foreach (var j in i.Value) {
                        sub.m_explosionParticleSystem.SetExplosionCell(j.Item1, j.Item2);
                    }
                }
                break;
            case EventType.Sound:
                sub.PlayExplosionSound(m_position, m_level, m_delay, true);
                break;
        }
    }
}
