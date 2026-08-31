using Engine;

namespace Game.Network;

/// <summary>
/// Coalesces authoritative terrain changes produced during one game update.
/// CommandBlock and ordinary gameplay therefore share one bounded wire path.
/// </summary>
public static class TerrainChangeBatchRuntime {
    private static readonly Dictionary<(int X, int Y, int Z), int> s_pending = [];

    public static void Enqueue(int x, int y, int z, int value) {
        if (!NetworkManager.IsServerRunning) {
            return;
        }

        s_pending[(x, y, z)] = value;
    }

    public static void Flush() {
        if (s_pending.Count == 0) {
            return;
        }

        List<CellChange> changes = s_pending
            .Select(pair => new CellChange {
                X = pair.Key.X,
                Y = pair.Key.Y,
                Z = pair.Key.Z,
                Value = pair.Value
            })
            .ToList();
        s_pending.Clear();

        for (int offset = 0; offset < changes.Count; offset += TerrainChangeCellListPacket.MaximumCellChanges) {
            int count = Math.Min(
                TerrainChangeCellListPacket.MaximumCellChanges,
                changes.Count - offset);
            NetworkManager.Queue(new TerrainChangeCellListPacket(
                changes.GetRange(offset, count)));
        }
    }

    public static void Clear() {
        s_pending.Clear();
    }
}
