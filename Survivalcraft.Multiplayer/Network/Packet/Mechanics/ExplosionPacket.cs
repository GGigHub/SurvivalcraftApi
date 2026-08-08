using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ExplosionPacket : Packet {
    public const int MaximumCellsPerChunk = 512;
    public const int MaximumChunksPerEvent = 64;
    public const int MaximumCellsPerEvent = MaximumCellsPerChunk * MaximumChunksPerEvent;

    public override byte ID => (byte)PacketType.Explosion;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public long m_eventId;
    public int m_chunkIndex;
    public int m_chunkCount;
    public readonly List<CellData> m_cells = [];
    public Vector3 m_position;
    public float m_pressure;
    public bool m_playExplosionSound;

    public struct CellData {
        public Point3 Point;
        public float Intensity;
    }

    public ExplosionPacket() { }

    private ExplosionPacket(
        long eventId,
        int chunkIndex,
        int chunkCount,
        IEnumerable<CellData> cells,
        Vector3 position,
        float pressure,
        bool playExplosionSound) {
        m_eventId = eventId;
        m_chunkIndex = chunkIndex;
        m_chunkCount = chunkCount;
        m_cells.AddRange(cells);
        m_position = position;
        m_pressure = pressure;
        m_playExplosionSound = playExplosionSound;
    }

    public static IReadOnlyList<ExplosionPacket> CreateChunks(
        Dictionary<Point2, List<(Point3, float)>> cells,
        long eventId,
        Vector3 position,
        float pressure,
        bool playExplosionSound) {
        ArgumentNullException.ThrowIfNull(cells);
        if (eventId <= 0) {
            throw new ArgumentOutOfRangeException(nameof(eventId));
        }
        if (!IsValidPosition(position)
            || !float.IsFinite(pressure)
            || pressure is <= 0f or > 100_000_000f) {
            throw new ArgumentOutOfRangeException(nameof(pressure));
        }

        CellData[] flattened = cells
            .OrderBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Y)
            .SelectMany(pair => pair.Value
                .OrderBy(value => value.Item1.X)
                .ThenBy(value => value.Item1.Y)
                .ThenBy(value => value.Item1.Z)
                .Select(value => new CellData {
                    Point = value.Item1,
                    Intensity = value.Item2
                }))
            .Where(IsValidCell)
            .Take(MaximumCellsPerEvent)
            .ToArray();
        if (flattened.Length == 0) {
            return [];
        }

        int chunkCount = (flattened.Length + MaximumCellsPerChunk - 1)
            / MaximumCellsPerChunk;
        List<ExplosionPacket> packets = new(chunkCount);
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
            packets.Add(new ExplosionPacket(
                eventId,
                chunkIndex,
                chunkCount,
                flattened
                    .Skip(chunkIndex * MaximumCellsPerChunk)
                    .Take(MaximumCellsPerChunk),
                position,
                pressure,
                playExplosionSound));
        }
        return packets;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_eventId);
        archive.Value(ref m_chunkIndex);
        archive.Value(ref m_chunkCount);
        int count = archive.IsWriting ? m_cells.Count : 0;
        archive.Value(ref count);
        if (count is < 1 or > MaximumCellsPerChunk) {
            throw new InvalidDataException(
                $"ExplosionPacket contains invalid cell count: {count}.");
        }
        if (archive.IsReading) {
            m_cells.Clear();
        }
        for (int index = 0; index < count; index++) {
            CellData data = archive.IsWriting ? m_cells[index] : default;
            archive.Value(ref data.Point);
            archive.Value(ref data.Intensity);
            if (archive.IsReading) {
                m_cells.Add(data);
            }
        }
        archive.Value(ref m_position);
        archive.Value(ref m_pressure);
        archive.Value(ref m_playExplosionSound);
    }

    public override void Handle(bool isServer) {
        Project project = GameManager.Project;
        if (isServer
            || project == null
            || !IsValidState()
            || !ExplosionEventTracker.TryAcceptCells(
                project,
                m_eventId,
                m_chunkIndex,
                m_chunkCount,
                m_cells.Count)) {
            return;
        }

        SubsystemExplosions subsystem = project.FindSubsystem<SubsystemExplosions>();
        if (subsystem == null) {
            return;
        }
        foreach (CellData cell in m_cells) {
            subsystem.m_explosionParticleSystem.SetExplosionCell(
                cell.Point,
                cell.Intensity);
        }
        if (m_playExplosionSound
            && ExplosionEventTracker.TryAcceptSound(project, m_eventId)) {
            subsystem.PlayExplosionSound(
                m_position,
                m_pressure,
                playExplosionSound: true);
        }
    }

    internal bool IsValidState() =>
        m_eventId > 0
        && m_chunkCount is > 0 and <= MaximumChunksPerEvent
        && m_chunkIndex >= 0
        && m_chunkIndex < m_chunkCount
        && m_cells.Count is > 0 and <= MaximumCellsPerChunk
        && m_cells.All(IsValidCell)
        && IsValidPosition(m_position)
        && float.IsFinite(m_pressure)
        && m_pressure is > 0f and <= 100_000_000f;

    private static bool IsValidCell(CellData cell) =>
        cell.Point.Y is >= 0 and < 256
        && Math.Abs((long)cell.Point.X) <= 16_777_216
        && Math.Abs((long)cell.Point.Z) <= 16_777_216
        && float.IsFinite(cell.Intensity)
        && cell.Intensity is > 0f and <= 1024f;

    private static bool IsValidPosition(Vector3 position) =>
        float.IsFinite(position.X)
        && float.IsFinite(position.Y)
        && float.IsFinite(position.Z)
        && Math.Abs((double)position.X) <= 16_777_216
        && position.Y is >= -256f and <= 512f
        && Math.Abs((double)position.Z) <= 16_777_216;
}
