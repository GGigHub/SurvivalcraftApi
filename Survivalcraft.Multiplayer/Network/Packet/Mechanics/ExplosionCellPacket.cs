using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ExplosionCellPacket : Packet {
    public const int MaximumCellsPerChunk = 512;
    public const int MaximumChunksPerEvent = 64;
    public const int MaximumCellsPerEvent = MaximumCellsPerChunk * MaximumChunksPerEvent;

    public override byte ID => (byte)PacketType.ExplosionCell;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public long m_eventId;
    public int m_chunkIndex;
    public int m_chunkCount;
    public readonly List<CellData> m_cells = [];

    public struct CellData {
        public Point3 Point;
        public float Intensity;
    }

    public ExplosionCellPacket() { }

    private ExplosionCellPacket(
        long eventId,
        int chunkIndex,
        int chunkCount,
        IEnumerable<CellData> cells) {
        m_eventId = eventId;
        m_chunkIndex = chunkIndex;
        m_chunkCount = chunkCount;
        m_cells.AddRange(cells);
    }

    public static IReadOnlyList<ExplosionCellPacket> CreateChunks(
        Dictionary<Point2, List<(Point3, float)>> cells,
        long eventId) {
        ArgumentNullException.ThrowIfNull(cells);
        if (eventId <= 0) {
            throw new ArgumentOutOfRangeException(nameof(eventId));
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
        int chunkCount = (flattened.Length + MaximumCellsPerChunk - 1) / MaximumCellsPerChunk;
        List<ExplosionCellPacket> packets = new(chunkCount);
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
            packets.Add(new ExplosionCellPacket(
                eventId,
                chunkIndex,
                chunkCount,
                flattened.Skip(chunkIndex * MaximumCellsPerChunk).Take(MaximumCellsPerChunk)));
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
            throw new InvalidDataException($"ExplosionCellPacket contains invalid cell count: {count}.");
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
    }

    public override void Handle(bool isServer) {
        Project project = GameManager.Project;
        if (isServer) {
            return;
        }
        if (project == null
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
            subsystem.m_explosionParticleSystem.SetExplosionCell(cell.Point, cell.Intensity);
        }
    }

    internal bool IsValidState() =>
        m_eventId > 0
        && m_chunkCount is > 0 and <= MaximumChunksPerEvent
        && m_chunkIndex >= 0
        && m_chunkIndex < m_chunkCount
        && m_cells.Count is > 0 and <= MaximumCellsPerChunk
        && m_cells.All(IsValidCell);

    private static bool IsValidCell(CellData cell) =>
        cell.Point.Y is >= 0 and < 256
        && Math.Abs((long)cell.Point.X) <= 16_777_216
        && Math.Abs((long)cell.Point.Z) <= 16_777_216
        && float.IsFinite(cell.Intensity)
        && cell.Intensity is > 0f and <= 1024f;
}
