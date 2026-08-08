using Engine;

namespace Game.Network;

/// <summary>
/// Keeps client-side predicted digs correlated with server terrain updates.
/// A request is deliberately delayed until the client has completed its local
/// prediction and the normal terrain confirmation has had time to arrive.
/// Recovery is coordinate-based and bounded by the server-side recovery cube.
/// </summary>
public static class NetworkDigConfirmationRuntime {
    private const int ClientConfirmationTimeoutFrames = 120;
    private const int ClientRequestTimeoutFrames = 240;
    private const int ServerRequestTimeoutFrames = 180;
    internal const int MaximumServerPendingRequests = 256;
    internal const int MaximumServerPendingRequestsPerPlayer = 16;

    private static int s_nextActionId;
    private static readonly Dictionary<int, PendingClientDig> s_clientPending = [];
    private static readonly Dictionary<ComponentMiner, PendingClientDig> s_clientActive = [];
    private static readonly NetworkDigServerRequestWindow s_serverRequests = new(
        MaximumServerPendingRequests,
        MaximumServerPendingRequestsPerPlayer);

    private sealed class PendingClientDig {
        public int ActionId;
        public ComponentMiner Miner;
        public ComponentPlayer Player;
        public CellFace CellFace;
        public int OldValue;
        public int ExpectedValue;
        public bool CompletedLocally;
        public bool RequestSent;
        public int DeadlineFrame;
    }

    public static void ObserveMinerDig(ComponentMiner miner, TerrainRaycastResult raycastResult) {
        if (!IsClientOnly
            || miner?.ComponentPlayer?.PlayerData?.IsMainPlayer != true
            || !TryDescribeDig(miner, raycastResult, out DigDescription description)) {
            return;
        }

        if (s_clientActive.TryGetValue(miner, out PendingClientDig active)
            && SameCell(active.CellFace, description.CellFace)) {
            return;
        }
        if (active != null) {
            RemoveClientPending(active);
        }

        PendingClientDig pending = new() {
            ActionId = NextClientActionId(),
            Miner = miner,
            Player = miner.ComponentPlayer,
            CellFace = description.CellFace,
            OldValue = description.OldValue,
            ExpectedValue = description.ExpectedValue,
            DeadlineFrame = int.MaxValue
        };
        s_clientPending[pending.ActionId] = pending;
        s_clientActive[miner] = pending;
    }

    public static void ObserveBlockDug(
        ComponentMiner miner,
        BlockPlacementData digValue,
        int cellValue
    ) {
        if (IsClientOnly) {
            if (miner?.ComponentPlayer?.PlayerData?.IsMainPlayer == true
                && s_clientActive.TryGetValue(miner, out PendingClientDig pending)
                && SameCell(pending.CellFace, digValue.CellFace)) {
                pending.CompletedLocally = true;
                pending.DeadlineFrame = Time.FrameIndex + ClientConfirmationTimeoutFrames;
            }
            return;
        }

        if (!NetworkManager.IsServerRunning) {
            return;
        }

        int playerIndex = miner?.ComponentPlayer?.PlayerData?.PlayerIndex ?? -1;
        if (playerIndex < 0) {
            return;
        }

        int actualValue = miner.m_subsystemTerrain.Terrain.GetCellValue(
            digValue.CellFace.X,
            digValue.CellFace.Y,
            digValue.CellFace.Z
        );
        MatchServerRequests(playerIndex, digValue.CellFace, actualValue);
    }

    public static void ObserveClientTerrainChange(int x, int y, int z, int value) {
        if (!IsClientOnly) {
            return;
        }

        foreach (PendingClientDig pending in s_clientPending.Values
                     .Where(pending => SameCell(pending.CellFace, x, y, z))
                     .ToArray()) {
            // Any server terrain packet for this cell is authoritative. An
            // equal value is a normal confirmation; a different value means
            // the server corrected the prediction.
            RemoveClientPending(pending);
        }
    }

    public static void ReceiveServerRequest(PlayerDigRequestPacket packet, PlayerData playerData) {
        if (!NetworkManager.IsServerRunning
            || packet == null
            || playerData?.ComponentPlayer == null
            || packet.ActionId <= 0) {
            return;
        }

        int playerIndex = playerData.PlayerIndex;
        if (s_serverRequests.Contains(playerIndex, packet.ActionId)) {
            return;
        }

        int currentValue = GetCurrentValue(packet.CellFace);
        if (ValuesEqual(currentValue, packet.ExpectedValue)) {
            SendConfirmation(packet, currentValue, packet.ExpectedValue, playerIndex);
            return;
        }

        if (!ValuesEqual(currentValue, packet.OldValue)) {
            SendConfirmation(
                packet,
                currentValue,
                packet.ExpectedValue,
                playerIndex,
                false,
                PlayerDigRequestFailureReason.ValueMismatch
            );
            return;
        }

        if (!s_serverRequests.TryAdd(packet, playerIndex, Time.FrameIndex)) {
            SendConfirmation(
                packet,
                currentValue,
                packet.ExpectedValue,
                playerIndex,
                false,
                PlayerDigRequestFailureReason.InvalidRequest
            );
            return;
        }

        if (!TryRecoverServerDig(packet, playerData.ComponentPlayer)
            && s_serverRequests.TryRemove(
                playerIndex,
                packet.ActionId,
                out _)) {
            SendConfirmation(
                packet,
                GetCurrentValue(packet.CellFace),
                packet.ExpectedValue,
                playerIndex,
                false,
                PlayerDigRequestFailureReason.InvalidRequest
            );
        }
    }

    public static void ReceiveClientConfirmation(PlayerDigConfirmationPacket packet) {
        if (!IsClientOnly
            || packet == null
            || !s_clientPending.TryGetValue(packet.ActionId, out PendingClientDig pending)
            || packet.m_fromPlayerIndex != pending.Player.PlayerData.PlayerIndex
            || !SameCell(packet.CellFace, pending.CellFace)
            || !ValuesEqual(packet.ExpectedValue, pending.ExpectedValue)) {
            return;
        }

        RemoveClientPending(pending);
        if (!packet.Success) {
            Log.Warning(
                $"Server dig confirmation failed: action={packet.ActionId}, "
                + $"cell=({packet.CellFace.X},{packet.CellFace.Y},{packet.CellFace.Z}), "
                + $"serverValue={packet.ActualValue}, reason={packet.FailureReason}"
            );
        }
    }

    public static void Tick() {
        int frame = Time.FrameIndex;

        if (IsClientOnly) {
            foreach (PendingClientDig pending in s_clientPending.Values.ToArray()) {
                if (!pending.CompletedLocally || pending.DeadlineFrame > frame) {
                    continue;
                }

                if (!pending.RequestSent) {
                    pending.RequestSent = true;
                    pending.DeadlineFrame = frame + ClientRequestTimeoutFrames;
                    try {
                        NetworkManager.Queue(new PlayerDigRequestPacket(
                            pending.ActionId,
                            pending.Player,
                            pending.CellFace,
                            pending.OldValue,
                            pending.ExpectedValue
                        ));
                    }
                    catch (Exception exception) {
                        Log.Warning($"Could not send dig confirmation request: {exception.Message}");
                        RemoveClientPending(pending);
                    }
                }
                else {
                    RemoveClientPending(pending);
                }
            }
        }

        if (NetworkManager.IsServerRunning) {
            foreach (NetworkDigServerRequest request in
                     s_serverRequests.TakeExpired(frame, ServerRequestTimeoutFrames)) {
                SendConfirmation(
                    request.Packet,
                    GetCurrentValue(request.Packet.CellFace),
                    request.Packet.ExpectedValue,
                    request.PlayerIndex,
                    false,
                    PlayerDigRequestFailureReason.ServerDidNotObserve
                );
            }
        }
    }

    public static void ClearClientState() {
        s_nextActionId = 0;
        s_clientPending.Clear();
        s_clientActive.Clear();
    }

    public static void ClearServerSession(int playerIndex) {
        if (playerIndex >= 0) {
            s_serverRequests.ClearPlayer(playerIndex);
        }
    }

    public static void Reset() {
        ClearClientState();
        s_serverRequests.Clear();
    }

    private static bool TryRecoverServerDig(PlayerDigRequestPacket packet, ComponentPlayer player) {
        ComponentMiner miner = player.ComponentMiner;
        if (miner == null
            || !TryResolveAuthoritativeDig(
                player,
                packet.CellFace,
                out BlockPlacementData authoritativeDig)) {
            return false;
        }

        int currentValue = GetCurrentValue(packet.CellFace);
        if (!ValuesEqual(currentValue, packet.OldValue)
            || !ValuesEqual(authoritativeDig.Value, packet.ExpectedValue)) {
            return false;
        }

        return miner.DigAtCell(authoritativeDig.CellFace, authoritativeDig.Value);
    }

    private static bool TryResolveAuthoritativeDig(
        ComponentPlayer player,
        CellFace requestedCell,
        out BlockPlacementData authoritativeDig
    ) {
        authoritativeDig = default;
        try {
            ComponentMiner miner = player.ComponentMiner;
            SubsystemTerrain terrain = miner.m_subsystemTerrain;
            SubsystemGameInfo gameInfo = player.Project.FindSubsystem<SubsystemGameInfo>(true);
            Vector3 eyePosition = player.ComponentCreatureModel.EyePosition;
            Vector3 cellCenter = new(
                requestedCell.X + 0.5f,
                requestedCell.Y + 0.5f,
                requestedCell.Z + 0.5f);
            Vector3 offset = cellCenter - eyePosition;
            float reach = gameInfo.WorldSettings.GameMode == GameMode.Creative
                ? SettingsManager.CreativeReach
                : 5f;
            reach = Math.Min(reach, SettingsManager.VisibilityRange);
            if (offset.LengthSquared() <= 0.0001f
                || offset.LengthSquared() > (reach + 1f) * (reach + 1f)) {
                return false;
            }

            object hit = miner.Raycast(
                new Ray3(eyePosition, Vector3.Normalize(offset)),
                RaycastMode.Digging,
                raycastTerrain: true,
                raycastBodies: false,
                raycastMovingBlocks: false,
                Reach: reach + 1f);
            if (hit is not TerrainRaycastResult raycastResult
                || !SameCell(raycastResult.CellFace, requestedCell)) {
                return false;
            }

            int currentValue = terrain.Terrain.GetCellValue(
                requestedCell.X,
                requestedCell.Y,
                requestedCell.Z);
            int contents = Terrain.ExtractContents(currentValue);
            if (contents <= 0 || contents >= BlocksManager.Blocks.Length) {
                return false;
            }
            authoritativeDig = BlocksManager.Blocks[contents].GetDigValue(
                terrain,
                miner,
                currentValue,
                miner.ActiveBlockValue,
                raycastResult);
            return SameCell(authoritativeDig.CellFace, requestedCell);
        }
        catch {
            return false;
        }
    }

    private static void MatchServerRequests(int playerIndex, CellFace cellFace, int actualValue) {
        foreach (NetworkDigServerRequest request in
                 s_serverRequests.TakeMatching(playerIndex, cellFace)) {
            SendConfirmation(
                request.Packet,
                actualValue,
                request.Packet.ExpectedValue,
                playerIndex,
                ValuesEqual(actualValue, request.Packet.ExpectedValue),
                ValuesEqual(actualValue, request.Packet.ExpectedValue)
                    ? PlayerDigRequestFailureReason.None
                    : PlayerDigRequestFailureReason.ValueMismatch
            );
        }
    }

    private static void SendConfirmation(
        PlayerDigRequestPacket request,
        int actualValue,
        int expectedValue,
        int playerIndex,
        bool success = true,
        PlayerDigRequestFailureReason failureReason = PlayerDigRequestFailureReason.None
    ) {
        ClientSession session = NetworkManager.ServerSessions
            .FirstOrDefault(item => item.PlayerIndex == playerIndex);
        if (session == null) {
            return;
        }

        bool valueMatches = ValuesEqual(actualValue, expectedValue);
        bool finalSuccess = success && valueMatches;
        if (!finalSuccess && failureReason == PlayerDigRequestFailureReason.None) {
            failureReason = PlayerDigRequestFailureReason.ValueMismatch;
        }

        NetworkManager.Queue(new PlayerDigConfirmationPacket(
            request,
            actualValue,
            finalSuccess,
            failureReason
        ) {
            To = session
        });
    }

    private static void RemoveClientPending(PendingClientDig pending) {
        s_clientPending.Remove(pending.ActionId);
        if (s_clientActive.TryGetValue(pending.Miner, out PendingClientDig active)
            && active.ActionId == pending.ActionId) {
            s_clientActive.Remove(pending.Miner);
        }
    }

    private static int NextClientActionId() {
        do {
            s_nextActionId = s_nextActionId == int.MaxValue ? 1 : s_nextActionId + 1;
        }
        while (s_clientPending.ContainsKey(s_nextActionId));
        return s_nextActionId;
    }

    private static bool TryDescribeDig(
        ComponentMiner miner,
        TerrainRaycastResult raycastResult,
        out DigDescription description
    ) {
        description = default;
        try {
            SubsystemTerrain terrain = miner?.m_subsystemTerrain;
            if (terrain?.Terrain == null) {
                return false;
            }

            int hitValue = terrain.Terrain.GetCellValue(
                raycastResult.CellFace.X,
                raycastResult.CellFace.Y,
                raycastResult.CellFace.Z
            );
            if (Terrain.ExtractContents(hitValue) == 0) {
                return false;
            }

            Block block = BlocksManager.Blocks[Terrain.ExtractContents(hitValue)];
            BlockPlacementData digValue = block.GetDigValue(
                terrain,
                miner,
                hitValue,
                miner.ActiveBlockValue,
                raycastResult
            );
            int oldValue = terrain.Terrain.GetCellValue(
                digValue.CellFace.X,
                digValue.CellFace.Y,
                digValue.CellFace.Z
            );
            if (Terrain.ExtractContents(oldValue) == 0) {
                return false;
            }

            description = new DigDescription {
                CellFace = digValue.CellFace,
                OldValue = oldValue,
                ExpectedValue = digValue.Value
            };
            return true;
        }
        catch {
            return false;
        }
    }

    private static int GetCurrentValue(CellFace cellFace) {
        try {
            return GameManager.Project?.FindSubsystem<SubsystemTerrain>()?.Terrain
                ?.GetCellValue(cellFace.X, cellFace.Y, cellFace.Z) ?? 0;
        }
        catch {
            return 0;
        }
    }

    private static bool ValuesEqual(int first, int second) =>
        Terrain.ReplaceLight(first, 0) == Terrain.ReplaceLight(second, 0);

    private static bool SameCell(CellFace first, CellFace second) =>
        first.X == second.X && first.Y == second.Y && first.Z == second.Z;

    private static bool SameCell(CellFace cellFace, int x, int y, int z) =>
        cellFace.X == x && cellFace.Y == y && cellFace.Z == z;

    private static bool IsClientOnly => NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;

    private struct DigDescription {
        public CellFace CellFace;
        public int OldValue;
        public int ExpectedValue;
    }
}
