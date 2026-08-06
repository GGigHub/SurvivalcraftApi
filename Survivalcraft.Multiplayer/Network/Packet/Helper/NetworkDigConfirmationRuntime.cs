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
    private const float MaxEyePositionDrift = 32f;

    private static int s_nextActionId;
    private static readonly Dictionary<int, PendingClientDig> s_clientPending = [];
    private static readonly Dictionary<ComponentMiner, PendingClientDig> s_clientActive = [];
    private static readonly Dictionary<ServerRequestKey, ServerRequest> s_serverRequests = [];

    private readonly record struct ServerRequestKey(int PlayerIndex, int ActionId);

    private sealed class PendingClientDig {
        public int ActionId;
        public ComponentMiner Miner;
        public ComponentPlayer Player;
        public CellFace CellFace;
        public int OldValue;
        public int ExpectedValue;
        public Vector3 EyePosition;
        public bool CompletedLocally;
        public bool RequestSent;
        public int DeadlineFrame;
    }

    private sealed class ServerRequest {
        public PlayerDigRequestPacket Packet;
        public int PlayerIndex;
        public int CreatedFrame;
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

        PendingClientDig pending = new() {
            ActionId = ++s_nextActionId,
            Miner = miner,
            Player = miner.ComponentPlayer,
            CellFace = description.CellFace,
            OldValue = description.OldValue,
            ExpectedValue = description.ExpectedValue,
            EyePosition = miner.ComponentPlayer.ComponentCreatureModel.EyePosition,
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
                pending.EyePosition = miner.ComponentPlayer.ComponentCreatureModel.EyePosition;
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
            if (SameContents(value, pending.ExpectedValue)) {
                RemoveClientPending(pending);
                continue;
            }
            if (!SameContents(value, pending.OldValue)) {
                RemoveClientPending(pending);
            }
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
        ServerRequestKey key = new(playerIndex, packet.ActionId);
        if (s_serverRequests.ContainsKey(key)) {
            return;
        }

        int currentValue = GetCurrentValue(packet.CellFace);
        if (ValuesEqual(currentValue, packet.ExpectedValue)) {
            SendConfirmation(packet, currentValue, packet.ExpectedValue, playerIndex);
            return;
        }

        if (!SameContents(currentValue, packet.OldValue)) {
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

        s_serverRequests[key] = new ServerRequest {
            Packet = packet,
            PlayerIndex = playerIndex,
            CreatedFrame = Time.FrameIndex
        };

        if (!TryRecoverServerDig(packet, playerData.ComponentPlayer)
            && s_serverRequests.Remove(key)) {
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
            || !s_clientPending.TryGetValue(packet.ActionId, out PendingClientDig pending)) {
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
                            pending.ExpectedValue,
                            pending.EyePosition
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
            foreach (ServerRequest request in s_serverRequests.Values
                         .Where(request => frame - request.CreatedFrame >= ServerRequestTimeoutFrames)
                         .ToArray()) {
                ServerRequestKey key = new(request.PlayerIndex, request.Packet.ActionId);
                if (!s_serverRequests.Remove(key)) {
                    continue;
                }
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

    public static void Reset() {
        s_nextActionId = 0;
        s_clientPending.Clear();
        s_clientActive.Clear();
        s_serverRequests.Clear();
    }

    private static bool TryRecoverServerDig(PlayerDigRequestPacket packet, ComponentPlayer player) {
        ComponentMiner miner = player.ComponentMiner;
        if (miner == null || !IsWithinRecoveryCube(player, packet)) {
            return false;
        }

        int currentValue = GetCurrentValue(packet.CellFace);
        if (!SameContents(currentValue, packet.OldValue)) {
            return false;
        }

        return miner.DigAtCell(packet.CellFace, packet.ExpectedValue);
    }

    private static bool IsWithinRecoveryCube(ComponentPlayer player, PlayerDigRequestPacket packet) {
        Vector3 cellCenter = new(packet.CellFace.X + 0.5f, packet.CellFace.Y + 0.5f, packet.CellFace.Z + 0.5f);

        ComponentMiner miner = player.ComponentMiner;
        float reach = miner.m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Creative
            ? SettingsManager.CreativeReach
            : 5f;
        reach = Math.Min(reach, SettingsManager.VisibilityRange);
        if (Vector3.DistanceSquared(packet.EyePosition, cellCenter) > (reach + 1f) * (reach + 1f)) {
            return false;
        }

        Vector3 currentEye = player.ComponentCreatureModel.EyePosition;
        return Vector3.DistanceSquared(packet.EyePosition, currentEye) <= MaxEyePositionDrift * MaxEyePositionDrift;
    }

    private static void MatchServerRequests(int playerIndex, CellFace cellFace, int actualValue) {
        foreach (ServerRequest request in s_serverRequests.Values
                     .Where(request => request.PlayerIndex == playerIndex
                         && SameCell(request.Packet.CellFace, cellFace))
                     .ToArray()) {
            s_serverRequests.Remove(new ServerRequestKey(playerIndex, request.Packet.ActionId));
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

    private static bool TryDescribeDig(
        ComponentMiner miner,
        TerrainRaycastResult raycastResult,
        out DigDescription description
    ) {
        description = default;
        try {
            if (miner?.m_subsystemTerrain?.Terrain == null
                || !miner.TryGetDigOperation(
                    raycastResult,
                    out BlockPlacementData digValue,
                    out int oldValue
                )) {
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

    private static bool SameContents(int first, int second) =>
        Terrain.ExtractContents(first) == Terrain.ExtractContents(second);

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
