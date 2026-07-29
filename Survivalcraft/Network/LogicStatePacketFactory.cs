using Engine;
using GameEntitySystem;

namespace Game.Network;

internal static class LogicStatePacketFactory {
    public static Packet[] CreateEditableBaselines(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        Dictionary<NetworkLogicStateKey, NetworkLogicAuthoritativeState> states = [];
        foreach (NetworkLogicAuthoritativeState state
                 in NetworkLogicStateTracker.GetAuthoritativeStates(project)) {
            if (state.Key.Domain == NetworkLogicStateDomain.Editable
                && !state.Key.IsItem) {
                states[state.Key] = state;
            }
        }

        SubsystemMemoryBankBlockBehavior memory =
            project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
        if (memory != null) {
            foreach (Point3 point in memory.m_blocksData.Keys.ToArray()) {
                AddPersistedState(states, project, NetworkLogicElementKind.MemoryBank, point);
            }
        }
        SubsystemTruthTableCircuitBlockBehavior truth =
            project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
        if (truth != null) {
            foreach (Point3 point in truth.m_blocksData.Keys.ToArray()) {
                AddPersistedState(states, project, NetworkLogicElementKind.TruthTable, point);
            }
        }
        SubsystemSignBlockBehavior signs = project.FindSubsystem<SubsystemSignBlockBehavior>();
        if (signs != null) {
            foreach (Point3 point in signs.m_textsByPoint.Keys.ToArray()) {
                AddPersistedState(states, project, NetworkLogicElementKind.Sign, point);
            }
        }

        return states.Values
            .OrderBy(state => state.Key.ElementKind)
            .ThenBy(state => state.Key.Point.X)
            .ThenBy(state => state.Key.Point.Y)
            .ThenBy(state => state.Key.Point.Z)
            .Select(state => CreateEditableBaseline(project, state))
            .Where(packet => packet != null)
            .ToArray();
    }

    public static Packet CreateEditableBaseline(
        Project project,
        NetworkLogicAuthoritativeState state) {
        ArgumentNullException.ThrowIfNull(project);
        NetworkLogicStateKey key = state.Key;
        if (key.Domain != NetworkLogicStateDomain.Editable
            || key.IsItem
            || key.ElementKind == NetworkLogicElementKind.None
            || state.Revision < 0) {
            return null;
        }
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>();
        if (terrain == null
            || key.Point.Y < 0
            || key.Point.Y >= TerrainChunk.Height) {
            return null;
        }
        int value = terrain.Terrain.GetCellValue(
            key.Point.X,
            key.Point.Y,
            key.Point.Z);
        int contents = Terrain.ExtractContents(value);
        int data = Terrain.ExtractData(value);
        CellFace cellFace = new(key.Point, 0);
        EditableBlockPacketBase packet = key.ElementKind switch {
            NetworkLogicElementKind.AdjustableDelayGate
                when contents == AdjustableDelayGateBlock.Index =>
                new AdjustableDelayGatePacket(
                    cellFace,
                    false,
                    0,
                    0,
                    AdjustableDelayGateBlock.GetDelay(data)),
            NetworkLogicElementKind.Battery
                when contents == BatteryBlock.Index =>
                new BatteryVoltagePacket(
                    cellFace,
                    false,
                    0,
                    0,
                    BatteryBlock.GetVoltageLevel(data)),
            NetworkLogicElementKind.Button
                when contents == ButtonBlock.Index =>
                new ButtonVoltagePacket(
                    cellFace,
                    false,
                    0,
                    0,
                    ButtonBlock.GetVoltageLevel(data)),
            NetworkLogicElementKind.Switch
                when contents == SwitchBlock.Index =>
                new SwitchVoltagePacket(
                    cellFace,
                    false,
                    0,
                    0,
                    SwitchBlock.GetVoltageLevel(data)),
            NetworkLogicElementKind.Piston
                when contents == PistonBlock.Index =>
                new PistonDataPacket(cellFace, false, 0, 0, data),
            NetworkLogicElementKind.MemoryBank
                when contents == MemoryBankBlock.Index =>
                new MemoryBankDataPacket(
                    cellFace,
                    false,
                    0,
                    0,
                    project.FindSubsystem<SubsystemMemoryBankBlockBehavior>()
                        ?.GetBlockData(key.Point) ?? new MemoryBankData()),
            NetworkLogicElementKind.TruthTable
                when contents == TruthTableCircuitBlock.Index =>
                new TruthTableDataPacket(
                    cellFace,
                    false,
                    0,
                    0,
                    project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>()
                        ?.GetBlockData(key.Point) ?? new TruthTableData()),
            NetworkLogicElementKind.Sign
                when contents >= 0
                    && contents < BlocksManager.Blocks.Length
                    && BlocksManager.Blocks[contents] is SignBlock =>
                CreateSignPacket(project, key.Point),
            NetworkLogicElementKind.Dispenser
                when contents == DispenserBlock.Index =>
                new DispenserPacket(key.Point, GetDispenserFlag(data)),
            _ => null
        };
        if (packet == null) {
            return null;
        }
        packet.m_packetKind = EditableLogicPacketKind.State;
        packet.m_result = EditableLogicResultCode.Accepted;
        packet.m_requestId = 0;
        packet.m_expectedRevision = -1;
        packet.m_revision = state.Revision;
        packet.m_terrainRevision = TerrainRevisionTracker.GetAuthoritativeRevision(
            terrain,
            key.Point.X >> 4,
            key.Point.Z >> 4);
        return packet;
    }

    private static SignBlockPacket CreateSignPacket(Project project, Point3 point) {
        SignData data = project.FindSubsystem<SubsystemSignBlockBehavior>()
            ?.GetSignData(point) ?? new SignData { Url = string.Empty };
        return new SignBlockPacket(
            point,
            data.Lines,
            data.Colors,
            data.Url ?? string.Empty);
    }

    private static byte GetDispenserFlag(int data) =>
        (byte)((DispenserBlock.GetMode(data) == DispenserBlock.Mode.Shoot ? 1 : 0)
            | (DispenserBlock.GetAcceptsDrops(data) ? 2 : 0));

    private static void AddPersistedState(
        Dictionary<NetworkLogicStateKey, NetworkLogicAuthoritativeState> states,
        Project project,
        NetworkLogicElementKind kind,
        Point3 point) {
        NetworkLogicStateKey key = NetworkLogicStateKey.Editable(
            kind,
            false,
            0,
            0,
            point);
        if (!states.ContainsKey(key)) {
            states[key] = new NetworkLogicAuthoritativeState(
                key,
                NetworkLogicStateTracker.GetAuthoritativeRevision(project, key),
                0);
        }
    }
}
