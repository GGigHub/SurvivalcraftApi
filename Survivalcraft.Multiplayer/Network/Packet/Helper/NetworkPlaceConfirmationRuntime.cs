using Engine;

namespace Game.Network;

/// <summary>
/// Correlates a locally predicted placement with the authoritative server
/// result. A rejected placement is corrected only while the cell still holds
/// the value produced by that prediction, so a later terrain update cannot be
/// overwritten by an old confirmation.
/// </summary>
public static class NetworkPlaceConfirmationRuntime {
    private const int ClientConfirmationTimeoutFrames = 300;

    private static int s_nextActionId;
    private static readonly Dictionary<int, PendingClientPlace> s_clientPending = [];

    private sealed class PendingClientPlace {
        public int ActionId;
        public ComponentMiner Miner;
        public CellFace CellFace;
        public int OldValue;
        public int ExpectedValue;
        public int DeadlineFrame;
    }

    public static bool TryRegisterClientPrediction(
        ComponentPlayer player,
        out int actionId,
        out ComponentMiner.PlacementOperation operation
    ) {
        actionId = 0;
        operation = default;
        if (!IsClientOnly
            || player?.PlayerData?.IsMainPlayer != true
            || player.ComponentMiner?.LastPlacementOperation is not ComponentMiner.PlacementOperation placement) {
            return false;
        }

        actionId = ++s_nextActionId;
        operation = placement;
        s_clientPending[actionId] = new PendingClientPlace {
            ActionId = actionId,
            Miner = player.ComponentMiner,
            CellFace = placement.CellFace,
            OldValue = placement.OldValue,
            ExpectedValue = placement.Value,
            DeadlineFrame = Time.FrameIndex + ClientConfirmationTimeoutFrames
        };
        return true;
    }

    public static void ObserveServerPlacement(
        ComponentPlayer player,
        ComponentPlayer.InteractEventItem request,
        bool placed
    ) {
        if (!NetworkManager.IsServerRunning
            || player == null
            || !request.HasPlacePrediction
            || request.PlaceActionId <= 0) {
            return;
        }

        int actualValue = GetCurrentValue(player.ComponentMiner?.m_subsystemTerrain, request.PlaceCellFace);
        bool success = placed && ValuesEqual(actualValue, request.PlaceExpectedValue);
        ClientSession session = NetworkManager.ServerSessions
            .FirstOrDefault(item => item.PlayerIndex == player.PlayerData.PlayerIndex);
        if (session == null) {
            return;
        }

        NetworkManager.Queue(new PlayerPlaceConfirmationPacket(player, request, actualValue, success) {
            To = session
        });

        // Place removes the active item locally before this confirmation is
        // sent. The complete server slot state repairs rejected placements and
        // also covers races with another inventory operation.
        InventoryPacketHelpers.QueueInventorySync(player.ComponentMiner?.Inventory, session);
    }

    public static void ObserveClientTerrainChange(int x, int y, int z, int value) {
        if (!IsClientOnly) {
            return;
        }

        foreach (PendingClientPlace pending in s_clientPending.Values
                     .Where(pending => SameCell(pending.CellFace, x, y, z))
                     .ToArray()) {
            // A terrain packet is already authoritative. Removing the pending
            // entry prevents a later confirmation from reverting that newer
            // state.
            RemoveClientPending(pending);
        }
    }

    public static void ReceiveClientConfirmation(PlayerPlaceConfirmationPacket packet) {
        if (!IsClientOnly
            || packet == null
            || !s_clientPending.TryGetValue(packet.ActionId, out PendingClientPlace pending)) {
            return;
        }

        RemoveClientPending(pending);
        if (packet.Success) {
            return;
        }

        SubsystemTerrain subsystemTerrain = pending.Miner?.m_subsystemTerrain;
        if (subsystemTerrain?.Terrain == null
            || !SameCell(pending.CellFace, packet.CellFace)) {
            return;
        }

        int currentValue = subsystemTerrain.Terrain.GetCellValue(
            pending.CellFace.X,
            pending.CellFace.Y,
            pending.CellFace.Z
        );
        if (!ValuesEqual(currentValue, pending.ExpectedValue)) {
            // Another authoritative update already won. Never overwrite it
            // with a stale rejection response.
            return;
        }

        subsystemTerrain.ChangeCell(
            pending.CellFace.X,
            pending.CellFace.Y,
            pending.CellFace.Z,
            packet.ActualValue,
            true,
            movingBlock: null,
            fromNetwork: true
        );
    }

    public static void Tick() {
        if (!IsClientOnly) {
            return;
        }

        foreach (PendingClientPlace pending in s_clientPending.Values
                     .Where(pending => pending.DeadlineFrame <= Time.FrameIndex)
                     .ToArray()) {
            int currentValue = GetCurrentValue(pending.Miner?.m_subsystemTerrain, pending.CellFace);
            if (ValuesEqual(currentValue, pending.ExpectedValue)) {
                pending.Miner?.m_subsystemTerrain?.ChangeCell(
                    pending.CellFace.X,
                    pending.CellFace.Y,
                    pending.CellFace.Z,
                    pending.OldValue,
                    true,
                    movingBlock: null,
                    fromNetwork: true
                );
            }
            RemoveClientPending(pending);
        }
    }

    public static void Reset() {
        s_nextActionId = 0;
        s_clientPending.Clear();
    }

    private static int GetCurrentValue(SubsystemTerrain subsystemTerrain, CellFace cellFace) {
        try {
            return subsystemTerrain?.Terrain?.GetCellValue(cellFace.X, cellFace.Y, cellFace.Z) ?? 0;
        }
        catch {
            return 0;
        }
    }

    private static void RemoveClientPending(PendingClientPlace pending) {
        s_clientPending.Remove(pending.ActionId);
    }

    private static bool ValuesEqual(int first, int second) =>
        Terrain.ReplaceLight(first, 0) == Terrain.ReplaceLight(second, 0);

    private static bool SameCell(CellFace first, CellFace second) =>
        first.X == second.X && first.Y == second.Y && first.Z == second.Z;

    private static bool SameCell(CellFace cellFace, int x, int y, int z) =>
        cellFace.X == x && cellFace.Y == y && cellFace.Z == z;

    private static bool IsClientOnly => NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
}
