using Engine;

namespace Game.NetWork;

public class ExplosionCellPackage : IPackage {
    public override byte ID => (byte)PackageType.ExplosionCell;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public Dictionary<Point2, List<(Point3, float)>> m_cells;

    public ExplosionCellPackage() {
    }

    public ExplosionCellPackage(Dictionary<Point2, List<(Point3, float)>> cells) {
        m_cells = new Dictionary<Point2, List<(Point3, float)>>();
        foreach (var c in cells) {
            m_cells.Add(c.Key, c.Value);
        }
    }

    public override void Serialize(PackageSerializer archive) {
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
        if (isServer) return;
        var sub = GameManager.Project.FindSubsystem<SubsystemExplosions>();
        if (sub != null) {
            foreach (var i in m_cells) {
                foreach (var j in i.Value) {
                    sub.m_explosionParticleSystem.SetExplosionCell(j.Item1, j.Item2);
                }
            }
        }
    }
}