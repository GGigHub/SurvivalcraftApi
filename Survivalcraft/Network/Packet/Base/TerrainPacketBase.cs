using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network;

public abstract class TerrainPacketBase : Packet {
    public class TerrainChunkSyncData {
        public Point2 Coords;
        public long Revision;
        public List<CellValueChange> CellChanges = [];
        public List<ShaftValueChange> ShaftChanges = [];
    }

    public struct CellValueChange {
        public ushort Index;
        public int Value;
    }

    public struct ShaftValueChange {
        public ushort Index;
        public int Value;
    }

    public static void SerializePoint2List(PacketSerializer archive, ref List<Point2> points, bool useUShort) {
        int count = archive.IsWriting ? points.Count : 0;
        if (useUShort) {
            ushort shortCount = (ushort)count;
            archive.Value(ref shortCount);
            count = shortCount;
        }
        else {
            archive.Value(ref count);
        }
        if (archive.IsReading) {
            points = [];
            for (int i = 0; i < count; i++) {
                Point2 point = default;
                archive.Value(ref point);
                points.Add(point);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                Point2 point = points[i];
                archive.Value(ref point);
            }
        }
    }

    public static void SerializeChunkList(PacketSerializer archive, ref List<TerrainChunk> chunks) {
        ushort count = archive.IsWriting ? (ushort)chunks.Count : (ushort)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            chunks = new List<TerrainChunk>(count);
            for (int i = 0; i < count; i++) {
                ReadChunk(archive, chunks);
            }
        }
        else {
            foreach (var chunk in chunks) {
                WriteChunk(archive, chunk);
            }
        }
    }

    public static void SerializeChunkSyncDataList(PacketSerializer archive, ref List<TerrainChunk> chunks, ref List<TerrainChunkSyncData> syncData) {
        ushort count = archive.IsWriting
            ? checked((ushort)(chunks.Count > 0 ? chunks.Count : syncData.Count))
            : (ushort)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            syncData = new List<TerrainChunkSyncData>(count);
            for (int i = 0; i < count; i++) {
                TerrainChunkSyncData data = new();
                archive.Value(ref data.Coords);
                archive.Value(ref data.Revision);
                SerializeCellValueChanges(archive, ref data.CellChanges);
                SerializeShaftValueChanges(archive, ref data.ShaftChanges);
                syncData.Add(data);
            }
        }
        else {
            if (chunks.Count > 0) {
                SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
                foreach (TerrainChunk chunk in chunks) {
                    WriteChunkSyncData(archive, CreateChunkSyncData(subsystemTerrain, chunk));
                }
            }
            else {
                foreach (TerrainChunkSyncData data in syncData) {
                    WriteChunkSyncData(archive, data);
                }
            }
        }
    }

    private static void WriteChunkSyncData(
        PacketSerializer archive,
        TerrainChunkSyncData data) {
        archive.Value(ref data.Coords);
        archive.Value(ref data.Revision);
        SerializeCellValueChanges(archive, ref data.CellChanges);
        SerializeShaftValueChanges(archive, ref data.ShaftChanges);
    }

    public static void SerializeCellChangeList(PacketSerializer archive, ref List<CellChange> cellChanges) {
        int count = archive.IsWriting ? cellChanges.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            cellChanges = [];
            for (int i = 0; i < count; i++) {
                CellChange cellChange = new();
                archive.Value(ref cellChange.X);
                archive.Value(ref cellChange.Y);
                archive.Value(ref cellChange.Z);
                archive.Value(ref cellChange.Value);
                archive.Value(ref cellChange.Revision);
                cellChanges.Add(cellChange);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                CellChange cellChange = cellChanges[i];
                archive.Value(ref cellChange.X);
                archive.Value(ref cellChange.Y);
                archive.Value(ref cellChange.Z);
                archive.Value(ref cellChange.Value);
                archive.Value(ref cellChange.Revision);
            }
        }
    }

    public static void ApplyOneChunk(SubsystemTerrain subsystemTerrain, TerrainChunk chunk) {
        int x = chunk.Coords.X;
        int y = chunk.Coords.Y;
        TerrainChunk targetChunk = subsystemTerrain.Terrain.GetChunkAtCoords(x, y) ?? subsystemTerrain.Terrain.AllocateChunk(x, y);
        if (targetChunk != null) {
            targetChunk.Cells = chunk.Cells;
            targetChunk.Shafts = chunk.Shafts;
            targetChunk.State = TerrainChunkState.InvalidLight;
            targetChunk.ThreadState = TerrainChunkState.InvalidLight;
            targetChunk.IsLoaded = true;
            targetChunk.WasDowngraded = false;
            targetChunk.DowngradedState = null;
            targetChunk.UpgradedState = TerrainChunkState.InvalidLight;
            targetChunk.WasUpgraded = true;
            subsystemTerrain.TerrainUpdater.DowngradeChunkNeighborhoodState(targetChunk.Coords, 1, TerrainChunkState.InvalidLight, false);
        }
    }

    public static void ApplyOneChunkSyncData(SubsystemTerrain subsystemTerrain, TerrainChunkSyncData data) {
        TerrainChunk existingChunk = subsystemTerrain.Terrain.GetChunkAtCoords(
            data.Coords.X,
            data.Coords.Y);
        bool forceRefresh = existingChunk == null
            || !existingChunk.IsLoaded
            || existingChunk.ThreadState <= TerrainChunkState.InvalidContents4;
        TerrainRevisionApplyPlan<TerrainRevisionCellChange> plan =
            TerrainRevisionTracker.ReceiveSnapshot(
                subsystemTerrain,
                data.Coords.X,
                data.Coords.Y,
                data.Revision,
                forceRefresh);
        if (!plan.ApplySnapshot) {
            return;
        }

        ApplyOneChunkSyncDataContents(subsystemTerrain, data);
        foreach (TerrainRevisionCellChange change in plan.Changes) {
            ApplyCellChangeContents(subsystemTerrain, change);
        }
    }

    public static void ApplyRevisionedCellChange(
        SubsystemTerrain subsystemTerrain,
        int x,
        int y,
        int z,
        int value,
        long revision) {
        TerrainRevisionApplyPlan<TerrainRevisionCellChange> plan =
            TerrainRevisionTracker.ReceiveChange(
                subsystemTerrain,
                new TerrainRevisionCellChange(x, y, z, value, revision));
        foreach (TerrainRevisionCellChange change in plan.Changes) {
            ApplyCellChangeContents(subsystemTerrain, change);
        }
    }

    private static void ApplyOneChunkSyncDataContents(
        SubsystemTerrain subsystemTerrain,
        TerrainChunkSyncData data) {
        TerrainChunk targetChunk = subsystemTerrain.Terrain.GetChunkAtCoords(data.Coords.X, data.Coords.Y)
            ?? subsystemTerrain.Terrain.AllocateChunk(data.Coords.X, data.Coords.Y);
        if (targetChunk == null) {
            return;
        }
        GenerateChunkBaseContents(subsystemTerrain, targetChunk);
        foreach (CellValueChange change in data.CellChanges) {
            targetChunk.Cells[change.Index] = change.Value;
        }
        foreach (ShaftValueChange change in data.ShaftChanges) {
            targetChunk.Shafts[change.Index] = change.Value;
        }
        targetChunk.State = TerrainChunkState.InvalidLight;
        targetChunk.ThreadState = TerrainChunkState.InvalidLight;
        targetChunk.IsLoaded = true;
        targetChunk.WasDowngraded = false;
        targetChunk.DowngradedState = null;
        targetChunk.UpgradedState = TerrainChunkState.InvalidLight;
        targetChunk.WasUpgraded = true;
        subsystemTerrain.TerrainUpdater.DowngradeChunkNeighborhoodState(targetChunk.Coords, 1, TerrainChunkState.InvalidLight, false);
    }

    private static void ApplyCellChangeContents(
        SubsystemTerrain subsystemTerrain,
        TerrainRevisionCellChange change) {
        subsystemTerrain.ChangeCell(
            change.X,
            change.Y,
            change.Z,
            change.Value,
            updateModificationCounter: true,
            movingBlock: null,
            fromNetwork: true);
    }

    private static void WriteChunk(PacketSerializer archive, TerrainChunk chunk) {
        Point2 coords = chunk.Coords;
        archive.Value(ref coords);
        WriteChunkContents(archive, chunk);
    }

    private static void WriteChunkContents(PacketSerializer archive, TerrainChunk chunk) {
        for (int i = 0; i < chunk.Cells.Length; i++) {
            int value = chunk.Cells[i];
            archive.Value(ref value);
        }
        for (int i = 0; i < chunk.Shafts.Length; i++) {
            int shaft = chunk.Shafts[i];
            archive.Value(ref shaft);
        }
    }

    private static void ReadChunk(PacketSerializer archive, List<TerrainChunk> chunks) {
        Point2 coords = default;
        archive.Value(ref coords);
        chunks.Add(ReadChunkContents(archive, coords));
    }

    private static TerrainChunk ReadChunkContents(PacketSerializer archive, Point2 coords) {
        TerrainChunk chunk = new(null, coords.X, coords.Y);
        for (int i = 0; i < 65536; i++) {
            archive.Value(ref chunk.Cells[i]);
        }
        for (int i = 0; i < 256; i++) {
            archive.Value(ref chunk.Shafts[i]);
        }
        return chunk;
    }

    internal static TerrainChunkSyncData CreateChunkSyncData(SubsystemTerrain subsystemTerrain, TerrainChunk chunk) {
        TerrainChunk baseChunk = new(subsystemTerrain.Terrain, chunk.Coords.X, chunk.Coords.Y);
        try {
            GenerateChunkBaseContents(subsystemTerrain, baseChunk);
            TerrainChunkSyncData data = new() {
                Coords = chunk.Coords,
                Revision = TerrainRevisionTracker.GetAuthoritativeRevision(
                    subsystemTerrain,
                    chunk.Coords.X,
                    chunk.Coords.Y)
            };
            for (int i = 0; i < chunk.Cells.Length; i++) {
                int value = NormalizeCellValueForDelta(chunk.Cells[i]);
                if (value != NormalizeCellValueForDelta(baseChunk.Cells[i])) {
                    data.CellChanges.Add(new CellValueChange {
                        Index = (ushort)i,
                        Value = value
                    });
                }
            }
            for (int i = 0; i < chunk.Shafts.Length; i++) {
                int value = NormalizeShaftValueForDelta(chunk.Shafts[i]);
                if (value != NormalizeShaftValueForDelta(baseChunk.Shafts[i])) {
                    data.ShaftChanges.Add(new ShaftValueChange {
                        Index = (ushort)i,
                        Value = value
                    });
                }
            }
            return data;
        }
        finally {
            baseChunk.Dispose();
        }
    }

    private static void GenerateChunkBaseContents(SubsystemTerrain subsystemTerrain, TerrainChunk chunk) {
        subsystemTerrain.TerrainContentsGenerator.GenerateChunkContentsPass1(chunk);
        subsystemTerrain.TerrainContentsGenerator.GenerateChunkContentsPass2(chunk);
        subsystemTerrain.TerrainContentsGenerator.GenerateChunkContentsPass3(chunk);
        subsystemTerrain.TerrainContentsGenerator.GenerateChunkContentsPass4(chunk);
    }

    private static int NormalizeCellValueForDelta(int value) => Terrain.ReplaceLight(value, 0);

    private static int NormalizeShaftValueForDelta(int value) {
        int result = 0;
        result = Terrain.ReplaceTemperature(result, Terrain.ExtractTemperature(value));
        result = Terrain.ReplaceHumidity(result, Terrain.ExtractHumidity(value));
        return result;
    }

    private static void SerializeCellValueChanges(PacketSerializer archive, ref List<CellValueChange> changes) {
        int count = archive.IsWriting ? changes.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            changes = new List<CellValueChange>(count);
            for (int i = 0; i < count; i++) {
                CellValueChange change = default;
                archive.Value(ref change.Index);
                archive.Value(ref change.Value);
                changes.Add(change);
            }
        }
        else {
            for (int i = 0; i < changes.Count; i++) {
                CellValueChange change = changes[i];
                archive.Value(ref change.Index);
                archive.Value(ref change.Value);
            }
        }
    }

    private static void SerializeShaftValueChanges(PacketSerializer archive, ref List<ShaftValueChange> changes) {
        ushort count = archive.IsWriting ? (ushort)changes.Count : (ushort)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            changes = new List<ShaftValueChange>(count);
            for (int i = 0; i < count; i++) {
                ShaftValueChange change = default;
                archive.Value(ref change.Index);
                archive.Value(ref change.Value);
                changes.Add(change);
            }
        }
        else {
            for (int i = 0; i < changes.Count; i++) {
                ShaftValueChange change = changes[i];
                archive.Value(ref change.Index);
                archive.Value(ref change.Value);
            }
        }
    }
}
