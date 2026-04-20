using Engine;
using System.Collections.Generic;

namespace Game.NetWork.Packages;

public class ExplosionsPackage : IPackage
{
    public byte ID => (byte)PackageType.Explosion;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;

    public enum EventType
    {
        Sound,
        Cell
    }
    public Vector3 m_position;
    public float m_level;
    public float m_delay;
    public EventType m_type;
    public Dictionary<Point2, List<(Point3, float)>> m_cells;
    public ExplosionsPackage() { }

    public ExplosionsPackage(Vector3 position, float level, float delay)
    {
        m_type = EventType.Sound;
        m_position = position;
        m_level = level;
        m_delay = delay;
    }

    public ExplosionsPackage(Dictionary<Point2, List<(Point3, float)>> cells)
    {
        m_type = EventType.Cell;
        m_cells = new Dictionary<Point2, List<(Point3, float)>>();
        foreach (var c in cells) m_cells.Add(c.Key, c.Value);
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case EventType.Cell:
                writer.Write(m_cells.Count);
                foreach (var c in m_cells)
                {
                    writer.Write(c.Key);
                    writer.Write(c.Value.Count);
                    foreach (var cc in c.Value)
                    {
                        writer.Write(cc.Item1);
                        writer.Write(cc.Item2);
                    }
                }
                break;
            case EventType.Sound:
                writer.Write(m_position);
                writer.Write(m_level);
                writer.Write(m_delay);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<EventType>();
        switch (m_type)
        {
            case EventType.Cell:
                m_cells = new Dictionary<Point2, List<(Point3, float)>>();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    Point2 point = reader.ReadPoint2();
                    List<(Point3, float)> list = new List<(Point3, float)>();
                    int cnt = reader.ReadInt32();
                    for (int j = 0; j < cnt; j++)
                    {
                        Point3 point3 = reader.ReadPoint3();
                        float v = reader.ReadSingle();
                        list.Add((point3, v));
                    }
                    m_cells.Add(point, list);
                }
                break;
            case EventType.Sound:
                m_position = reader.ReadVector3();
                m_level = reader.ReadSingle();
                m_delay = reader.ReadSingle();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (isServer)
        {
            return;
        }
        var sub = projectNet.FindSubsystem<SubsystemExplosions>();
        switch (m_type)
        {
            case EventType.Cell:
                foreach (var i in m_cells)
                {
                    Point2 chunk = i.Key;
                    foreach (var j in i.Value)
                    {
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

