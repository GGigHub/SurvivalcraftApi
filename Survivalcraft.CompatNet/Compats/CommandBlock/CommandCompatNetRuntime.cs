using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Reflection bridge used by the in-memory CommandBlock patch. Keeping the
/// bridge independent from CommandBlock.dll lets CompatNet load without the
/// optional mod installed.
/// </summary>
public static class CommandCompatNetRuntime {
    public const int MaximumCommandLineChars = 8192;

    private const string CommandBlockBehaviorTypeName = "Command.SubsystemCommandBlockBehavior";
    private const string CommandBlockTypeName = "Command.CommandBlock";
    private const string SubmitResultTypeName = "Command.SubmitResult";

    private static readonly BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags AnyStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type s_submitResultType;
    private static FieldInfo s_commandElectricVoltageField;
    private static MethodInfo s_submitMethod;
    private static readonly Dictionary<Point3, float> s_lastElectricVoltages = [];
    private static readonly Dictionary<int, double> s_lastRequestTimes = [];
    private static readonly Dictionary<int, int> s_lastPlayerStateSequences = [];
    private static int s_nextRequestId;
    private static int s_nextPlayerStateSequence;
    [ThreadStatic]
    private static int s_authoritativeExecutionDepth;

    private static readonly HashSet<string> s_serverCommands = new(StringComparer.OrdinalIgnoreCase) {
        "place", "dig", "replace", "function", "copyblock", "moveblock", "moveset",
        "furniture", "build", "colorblock", "withdraw", "addnpc", "removenpc",
        "teleport", "spawn", "stats", "action", "creaturedata", "additem",
        "removeitem", "clearitem", "adddrop", "removedrop", "launchdrop", "explosion",
        "lightning", "weather", "skycolor", "temperature", "humidity", "gametime",
        "gamemode", "gamesettings", "worldsettings"
    };

    public static void Reset() {
        s_submitResultType = null;
        s_commandElectricVoltageField = null;
        s_submitMethod = null;
        s_lastElectricVoltages.Clear();
        s_lastRequestTimes.Clear();
        s_lastPlayerStateSequences.Clear();
        s_nextRequestId = 0;
        s_nextPlayerStateSequence = 0;
        s_authoritativeExecutionDepth = 0;
    }

    public static void ResetPlayerStateSequences() {
        s_lastPlayerStateSequences.Clear();
    }

    public static object SubmitCommandBridge(
        object subsystemCommand,
        string name,
        object commandData,
        bool judge) {
        if (IsClientOnly() && !judge && IsServerCommand(name, commandData)) {
            ComponentPlayer player = ReadField(subsystemCommand, "m_componentPlayer") as ComponentPlayer;
            if (player == null
                || !TryReadCommandData(commandData, out Point3 position, out string line)) {
                return CreateSubmitResult(1);
            }

            CommandExecuteRequest request = new() {
                RequestId = Interlocked.Increment(ref s_nextRequestId),
                Position = position,
                Judge = judge,
                Line = line
            };
            return CompatNetCore.QueueRequest(
                    player,
                    CommandCompatNetAdapter.StableAdapterId,
                    (ushort)CommandCompatNetOperation.ExecuteCommand,
                    CompatNetPayload.Write(request.Serialize))
                ? CreateSubmitResult(0)
                : CreateSubmitResult(1);
        }

        if (NetworkManager.IsServerRunning && s_authoritativeExecutionDepth == 0) {
            return InvokeWithSynchronization(
                subsystemCommand,
                name,
                commandData,
                judge,
                ReadField(subsystemCommand, "m_componentPlayer") as ComponentPlayer,
                captureState: true);
        }
        return InvokeSubmit(subsystemCommand, name, commandData, judge);
    }

    public static bool TryExecuteCommand(
        PlayerData playerData,
        CommandExecuteRequest request,
        out int resultCode) {
        resultCode = 1;
        if (!NetworkManager.IsServerRunning
            || playerData?.ComponentPlayer == null
            || request == null
            || !IsValidLineLength(request.Line)
            || !IsServerCommand(ExtractCommandName(request.Line))
            || !AcceptRequest(playerData.PlayerIndex)
            || !IsValidRequestPosition(playerData.ComponentPlayer, request.Position)) {
            return false;
        }

        object subsystemCommand = FindSubsystem("Command.SubsystemCommand");
        if (subsystemCommand == null
            || !TryCreateCommandData(request.Position, request.Line, out object commandData)) {
            return false;
        }

        bool valid = ReadField(commandData, "Valid") is bool isValid && isValid;
        bool outOfRange = ReadField(commandData, "OutRange") is bool isOutOfRange && isOutOfRange;
        if (!valid) {
            resultCode = 2;
            return false;
        }
        if (outOfRange) {
            resultCode = 3;
            return false;
        }

        string name = ReadField(commandData, "Name") as string;
        if (!IsServerCommand(name, commandData)) {
            return false;
        }

        object result = InvokeWithSynchronization(
            subsystemCommand,
            name,
            commandData,
            request.Judge,
            playerData.ComponentPlayer,
            captureState: true);
        resultCode = GetSubmitResultValue(result);
        return true;
    }

    public static void NotifyCommandBlockEdited(
        ComponentPlayer player,
        Point3 position,
        string line) {
        if (!IsClientOnly() || player == null || !IsValidLineLength(line)) {
            return;
        }

        CommandEditRequest request = new() {
            Position = position,
            Line = line ?? string.Empty
        };
        CompatNetCore.QueueRequest(
            player,
            CommandCompatNetAdapter.StableAdapterId,
            (ushort)CommandCompatNetOperation.EditBlock,
            CompatNetPayload.Write(request.Serialize));
    }

    public static void RequestElectricSnapshot(ComponentPlayer player) {
        if (!IsClientOnly() || player == null) {
            return;
        }

        CompatNetCore.QueueRequest(
            player,
            CommandCompatNetAdapter.StableAdapterId,
            (ushort)CommandCompatNetOperation.RequestElectricSnapshot,
            []);
    }

    /// <summary>
    /// Replaces Submit calls made by CommandElectricElement. Clients do not
    /// submit commands locally; the server's electricity simulation is the
    /// single executor and reports the output voltage back.
    /// </summary>
    public static object SubmitElectricCommandBridge(
        object subsystemCommand,
        string name,
        object commandData,
        bool judge) {
        if (IsClientOnly()) {
            return CreateSubmitResult(1);
        }

        object result = NetworkManager.IsServerRunning
            ? InvokeWithSynchronization(
                subsystemCommand,
                name,
                commandData,
                judge,
                ReadField(subsystemCommand, "m_componentPlayer") as ComponentPlayer,
                captureState: IsServerCommand(name, commandData))
            : InvokeSubmit(subsystemCommand, name, commandData, judge);
        if (TryReadCommandData(commandData, out Point3 position, out _)) {
            CommandElectricResult electricResult = new() {
                Position = position,
                Voltage = GetSubmitResultValue(result) == 0 ? 1f : 0f
            };
            if (IsElectricOutputMode(commandData)
                && (!s_lastElectricVoltages.TryGetValue(position, out float previous)
                    || previous != electricResult.Voltage)) {
                s_lastElectricVoltages[position] = electricResult.Voltage;
                CompatNetCore.QueueBroadcast(
                    CommandCompatNetAdapter.StableAdapterId,
                    (ushort)CommandCompatNetOperation.ElectricResult,
                    CompatNetPayload.Write(electricResult.Serialize));
            }
        }
        return result;
    }

    /// <summary>
    /// The command mod uses Task.Run for large builds and copy operations.
    /// Running those writes on the authoritative game thread keeps terrain and
    /// ECS state inside the same transaction as the command request.
    /// </summary>
    public static Task RunCommandTask(Action action) {
        if (action == null) {
            return Task.CompletedTask;
        }
        if (NetworkManager.IsServerRunning && s_authoritativeExecutionDepth > 0) {
            action();
            return Task.CompletedTask;
        }
        return Task.Run(action);
    }

    /// <summary>
    /// Replaces the command mod's direct Terrain.SetCellValueFast call. The
    /// command path uses ChangeCell so the normal server terrain hook and the
    /// bounded terrain batcher observe every write.
    /// </summary>
    public static void SetCommandTerrainCell(
        object terrain,
        int x,
        int y,
        int z,
        int value) {
        if (NetworkManager.IsServerRunning && s_authoritativeExecutionDepth > 0) {
            SubsystemTerrain subsystemTerrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
            if (subsystemTerrain != null) {
                subsystemTerrain.ChangeCell(x, y, z, value, true, null, false);
                return;
            }
        }

        MethodInfo method = terrain?.GetType().GetMethod(
            "SetCellValueFast",
            AnyInstance,
            null,
            [typeof(int), typeof(int), typeof(int), typeof(int)],
            null);
        method?.Invoke(terrain, [x, y, z, value]);
    }

    /// <summary>
    /// The client receives the authoritative electric output from the server.
    /// Skipping the original Simulate body prevents the local command element
    /// from overwriting that value on its next circuit tick.
    /// </summary>
    public static bool ShouldSkipElectricSimulation(object electricElement) {
        return IsClientOnly();
    }

    public static void SendElectricSnapshot(ClientSession to) {
        if (!NetworkManager.IsServerRunning || to == null) {
            return;
        }

        SubsystemElectricity electricity = GameManager.Project?.FindSubsystem<SubsystemElectricity>();
        if (electricity == null) {
            return;
        }

        List<CommandElectricResult> results = [];
        foreach (ElectricElement element in electricity.m_electricElements.Keys) {
            if (element.GetType().FullName != "Command.CommandElectricElement"
                || element.CellFaces.Count == 0) {
                continue;
            }

            CellFace cellFace = element.CellFaces[0];
            if (!IsElectricOutputMode(ReadField(element, "m_commandData"))) {
                continue;
            }

            FieldInfo voltageField = ResolveElectricVoltageField(element.GetType());
            if (voltageField?.GetValue(element) is float voltage) {
                results.Add(new CommandElectricResult {
                    Position = cellFace.Point,
                    Voltage = voltage
                });
            }
        }

        const int maxResultsPerPacket = 3000;
        for (int offset = 0; offset < results.Count; offset += maxResultsPerPacket) {
            CommandElectricSnapshot snapshot = new();
            snapshot.Results.AddRange(results.Skip(offset).Take(maxResultsPerPacket));
            CompatNetCore.QueueBroadcast(
                CommandCompatNetAdapter.StableAdapterId,
                (ushort)CommandCompatNetOperation.ElectricSnapshot,
                CompatNetPayload.Write(snapshot.Serialize),
                to: to);
        }
    }

    public static bool TryApplyCommandEdit(
        ComponentPlayer player,
        Point3 position,
        string line,
        out CommandBlockSync sync) {
        sync = null;
        if (player == null
            || !IsValidLineLength(line)
            || !IsWithinEditRange(player, position)) {
            return false;
        }

        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        if (terrain == null
            || !terrain.Terrain.IsCellValid(position.X, position.Y, position.Z)) {
            return false;
        }

        int cellValue = terrain.Terrain.GetCellValue(position.X, position.Y, position.Z);
        if (Terrain.ExtractContents(cellValue) != ResolveCommandBlockIndex()) {
            return false;
        }

        object behavior = FindSubsystem(CommandBlockBehaviorTypeName);
        object commandData = SetCommandData(behavior, position, line ?? string.Empty);
        if (commandData == null) {
            return false;
        }

        object mode = ReadField(commandData, "Mode");
        int newCellValue = SetWorkingMode(cellValue, mode);
        terrain.ChangeCell(
            position.X,
            position.Y,
            position.Z,
            newCellValue,
            true,
            null,
            false);

        string canonicalLine = ReadField(commandData, "Line") as string ?? string.Empty;
        sync = new CommandBlockSync {
            Position = position,
            Line = canonicalLine
        };
        return true;
    }

    public static void ApplyCommandBlockSync(CommandBlockSync sync) {
        if (sync == null) {
            return;
        }

        try {
            object behavior = FindSubsystem(CommandBlockBehaviorTypeName);
            SetCommandData(behavior, sync.Position, sync.Line ?? string.Empty);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to apply command block sync: {e}");
        }
    }

    public static void ApplyElectricResult(CommandElectricResult result) {
        if (result == null || !NetworkManager.IsClientRunning || NetworkManager.IsServerRunning) {
            return;
        }

        try {
            SubsystemElectricity electricity = GameManager.Project?.FindSubsystem<SubsystemElectricity>();
            ElectricElement element = electricity?.GetElectricElement(
                result.Position.X,
                result.Position.Y,
                result.Position.Z,
                0);
            if (element == null) {
                return;
            }

            FieldInfo voltageField = ResolveElectricVoltageField(element.GetType());
            voltageField?.SetValue(element, result.Voltage);
            electricity.QueueElectricElementConnectionsForSimulation(
                element,
                electricity.CircuitStep + 1);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to apply command electric result: {e}");
        }
    }

    public static void ApplyPlayerState(CommandPlayerStateSync state) {
        if (state == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || !IsValidPlayerState(state)
            || (s_lastPlayerStateSequences.TryGetValue(state.PlayerIndex, out int last)
                && state.Sequence <= last)) {
            return;
        }

        PlayerData playerData = GameManager.Project?.FindSubsystem<SubsystemPlayers>()
            ?.FindPlayerData(player => player.PlayerIndex == state.PlayerIndex);
        ComponentPlayer player = playerData?.ComponentPlayer;
        if (player == null) {
            return;
        }

        s_lastPlayerStateSequences[state.PlayerIndex] = state.Sequence;
        playerData.SpawnPosition = state.SpawnPosition;
        player.ComponentHealth.Health = state.Health;
        player.ComponentHealth.AttackResilience = state.AttackResilience;
        player.ComponentHealth.FallResilience = state.FallResilience;
        player.ComponentHealth.FireResilience = state.FireResilience;
        player.ComponentHealth.Air = state.Air;
        player.ComponentHealth.AirCapacity = state.AirCapacity;
        player.ComponentHealth.IsInvulnerable = state.IsInvulnerable;
        player.ComponentVitalStats.Food = state.Food;
        player.ComponentVitalStats.Sleep = state.Sleep;
        player.ComponentVitalStats.Stamina = state.Stamina;
        player.ComponentVitalStats.Wetness = state.Wetness;
        player.ComponentVitalStats.Temperature = state.Temperature;
        player.ComponentMiner.AttackPower = state.AttackPower;
        player.ComponentLocomotion.WalkSpeed = state.WalkSpeed;
        player.ComponentLocomotion.JumpSpeed = state.JumpSpeed;
        player.ComponentLocomotion.LadderSpeed = state.LadderSpeed;
        player.ComponentLocomotion.SwimSpeed = state.SwimSpeed;
        player.ComponentLocomotion.FlySpeed = state.FlySpeed;
        player.ComponentLocomotion.CreativeFlySpeed = state.CreativeFlySpeed;
    }

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }

    private static bool IsValidLineLength(string line) {
        return (line?.Length ?? 0) <= MaximumCommandLineChars;
    }

    private static bool IsServerCommand(string name) {
        return !string.IsNullOrWhiteSpace(name)
            && s_serverCommands.Contains(name.Trim());
    }

    private static bool IsServerCommand(string name, object commandData) {
        if (!IsServerCommand(name)) {
            return false;
        }
        // `stats` without a type is the command mod's local information
        // screen. Only its typed mutations need an authoritative round trip.
        return !string.Equals(name.Trim(), "stats", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ReadField(commandData, "Type") as string, "default", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCommandName(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return string.Empty;
        }
        string first = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        string[] parts = first.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : parts[^1];
    }

    private static bool AcceptRequest(int playerIndex) {
        double now = Time.RealTime;
        if (s_lastRequestTimes.TryGetValue(playerIndex, out double previous)
            && now - previous < 0.05) {
            return false;
        }
        s_lastRequestTimes[playerIndex] = now;
        return true;
    }

    private static bool IsValidRequestPosition(ComponentPlayer player, Point3 position) {
        if (position == Point3.Zero || player?.ComponentBody == null) {
            return true;
        }
        Vector3 center = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
        return Vector3.DistanceSquared(player.ComponentBody.Position, center) <= 4096f;
    }

    private static bool TryCreateCommandData(
        Point3 position,
        string line,
        out object commandData) {
        commandData = null;
        try {
            Type type = ResolveLoadedType("Command.CommandData");
            ConstructorInfo constructor = type?.GetConstructor(
                AnyInstance,
                null,
                [typeof(Point3), typeof(string)],
                null);
            commandData = constructor?.Invoke([position, line ?? string.Empty]);
            MethodInfo parse = type?.GetMethod("TrySetValue", AnyInstance, null, Type.EmptyTypes, null);
            parse?.Invoke(commandData, null);
            return commandData != null;
        }
        catch (Exception e) {
            Log.Warning($"[Survivalcraft.CompatNet] Command parse failed: {e.Message}");
            commandData = null;
            return false;
        }
    }

    private static object InvokeWithSynchronization(
        object subsystemCommand,
        string name,
        object commandData,
        bool judge,
        ComponentPlayer player,
        bool captureState) {
        bool root = s_authoritativeExecutionDepth == 0;
        ComponentPlayer contextPlayer = player
            ?? ReadField(subsystemCommand, "m_componentPlayer") as ComponentPlayer;
        InventorySnapshot inventories = root && captureState ? CaptureInventories() : null;
        Dictionary<ComponentPlayer, CommandPlayerStateSnapshot> playerStates =
            root && captureState ? CapturePlayerStates() : null;
        FieldInfo playerField = subsystemCommand?.GetType().GetField("m_componentPlayer", AnyInstance);
        FieldInfo gameModeField = subsystemCommand?.GetType().GetField("m_gameMode", AnyInstance);
        object oldPlayer = playerField?.GetValue(subsystemCommand);
        object oldGameMode = gameModeField?.GetValue(subsystemCommand);

        s_authoritativeExecutionDepth++;
        try {
            if (contextPlayer != null) {
                playerField?.SetValue(subsystemCommand, contextPlayer);
                object gameMode = contextPlayer.m_subsystemGameInfo?.WorldSettings?.GameMode;
                if (gameMode != null && gameModeField?.FieldType.IsInstanceOfType(gameMode) == true) {
                    gameModeField.SetValue(subsystemCommand, gameMode);
                }
            }
            return InvokeSubmit(subsystemCommand, name, commandData, judge);
        }
        finally {
            if (playerField != null) {
                playerField.SetValue(subsystemCommand, oldPlayer);
            }
            if (gameModeField != null) {
                gameModeField.SetValue(subsystemCommand, oldGameMode);
            }
            s_authoritativeExecutionDepth--;
            if (root) {
                SyncChangedInventories(inventories);
                SyncChangedPlayerStates(playerStates);
            }
        }
    }

    private sealed class InventorySnapshot {
        public readonly Dictionary<IInventory, int[]> Slots =
            new(ReferenceEqualityComparer.Instance);
    }

    private sealed class CommandPlayerStateSnapshot {
        public Vector3 BodyPosition;
        public Vector3 BodyVelocity;
        public Quaternion BodyRotation;
        public Vector3 SpawnPosition;
        public float Health;
        public float Food;
        public float Sleep;
        public float Stamina;
        public float Wetness;
        public float Temperature;
        public float AttackPower;
        public float AttackResilience;
        public float FallResilience;
        public float FireResilience;
        public float WalkSpeed;
        public float JumpSpeed;
        public float LadderSpeed;
        public float SwimSpeed;
        public float FlySpeed;
        public float CreativeFlySpeed;
        public float Air;
        public float AirCapacity;
        public bool IsInvulnerable;
    }

    private static InventorySnapshot CaptureInventories() {
        InventorySnapshot snapshot = new();
        if (GameManager.Project == null) {
            return snapshot;
        }
        foreach (Entity entity in GameManager.Project.Entities) {
            foreach (IInventory inventory in entity.FindComponents<IInventory>()) {
                int[] slots = new int[inventory.SlotsCount * 2];
                for (int i = 0; i < inventory.SlotsCount; i++) {
                    slots[i * 2] = inventory.GetSlotValue(i);
                    slots[i * 2 + 1] = inventory.GetSlotCount(i);
                }
                snapshot.Slots[inventory] = slots;
            }
        }
        return snapshot;
    }

    private static void SyncChangedInventories(InventorySnapshot before) {
        if (!NetworkManager.IsServerRunning || before == null) {
            return;
        }
        InventorySnapshot after = CaptureInventories();
        foreach ((IInventory inventory, int[] slots) in after.Slots) {
            if (!before.Slots.TryGetValue(inventory, out int[] oldSlots)
                || !slots.SequenceEqual(oldSlots)) {
                InventoryPacketHelpers.QueueInventorySync(inventory);
            }
        }
    }

    private static CommandPlayerStateSnapshot CapturePlayerState(ComponentPlayer player) {
        if (player?.ComponentBody == null
            || player.ComponentHealth == null
            || player.ComponentVitalStats == null
            || player.ComponentMiner == null
            || player.ComponentLocomotion == null
            || player.PlayerData == null) {
            return null;
        }
        return new CommandPlayerStateSnapshot {
            BodyPosition = player.ComponentBody.Position,
            BodyVelocity = player.ComponentBody.Velocity,
            BodyRotation = player.ComponentBody.Rotation,
            SpawnPosition = player.PlayerData.SpawnPosition,
            Health = player.ComponentHealth.Health,
            Food = player.ComponentVitalStats.Food,
            Sleep = player.ComponentVitalStats.Sleep,
            Stamina = player.ComponentVitalStats.Stamina,
            Wetness = player.ComponentVitalStats.Wetness,
            Temperature = player.ComponentVitalStats.Temperature,
            AttackPower = player.ComponentMiner.AttackPower,
            AttackResilience = player.ComponentHealth.AttackResilience,
            FallResilience = player.ComponentHealth.FallResilience,
            FireResilience = player.ComponentHealth.FireResilience,
            WalkSpeed = player.ComponentLocomotion.WalkSpeed,
            JumpSpeed = player.ComponentLocomotion.JumpSpeed,
            LadderSpeed = player.ComponentLocomotion.LadderSpeed,
            SwimSpeed = player.ComponentLocomotion.SwimSpeed,
            FlySpeed = player.ComponentLocomotion.FlySpeed,
            CreativeFlySpeed = player.ComponentLocomotion.CreativeFlySpeed,
            Air = player.ComponentHealth.Air,
            AirCapacity = player.ComponentHealth.AirCapacity,
            IsInvulnerable = player.ComponentHealth.IsInvulnerable
        };
    }

    private static Dictionary<ComponentPlayer, CommandPlayerStateSnapshot> CapturePlayerStates() {
        Dictionary<ComponentPlayer, CommandPlayerStateSnapshot> states =
            new(ReferenceEqualityComparer.Instance);
        foreach (ComponentPlayer player in GameManager.Project?.FindSubsystem<SubsystemPlayers>()?.ComponentPlayers
            ?? ReadOnlyList<ComponentPlayer>.Empty) {
            CommandPlayerStateSnapshot state = CapturePlayerState(player);
            if (state != null) {
                states[player] = state;
            }
        }
        return states;
    }

    private static void SyncChangedPlayerStates(
        Dictionary<ComponentPlayer, CommandPlayerStateSnapshot> before) {
        if (!NetworkManager.IsServerRunning || before == null) {
            return;
        }

        foreach ((ComponentPlayer player, CommandPlayerStateSnapshot oldState) in before) {
            CommandPlayerStateSnapshot after = CapturePlayerState(player);
            if (after == null || AreEqual(oldState, after)) {
                continue;
            }

            NetworkManager.Queue(new HealthSyncPacket(player.ComponentHealth));
            NetworkManager.Queue(new VitalStatSyncPacket(player.ComponentVitalStats));
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player, null, true));
            CommandPlayerStateSync sync = new() {
                Sequence = Interlocked.Increment(ref s_nextPlayerStateSequence),
                PlayerIndex = player.PlayerData.PlayerIndex,
                SpawnPosition = after.SpawnPosition,
                Health = after.Health,
                Food = after.Food,
                Sleep = after.Sleep,
                Stamina = after.Stamina,
                Wetness = after.Wetness,
                Temperature = after.Temperature,
                AttackPower = after.AttackPower,
                AttackResilience = after.AttackResilience,
                FallResilience = after.FallResilience,
                FireResilience = after.FireResilience,
                WalkSpeed = after.WalkSpeed,
                JumpSpeed = after.JumpSpeed,
                LadderSpeed = after.LadderSpeed,
                SwimSpeed = after.SwimSpeed,
                FlySpeed = after.FlySpeed,
                CreativeFlySpeed = after.CreativeFlySpeed,
                Air = after.Air,
                AirCapacity = after.AirCapacity,
                IsInvulnerable = after.IsInvulnerable
            };
            CompatNetCore.QueueBroadcast(
                CommandCompatNetAdapter.StableAdapterId,
                (ushort)CommandCompatNetOperation.PlayerState,
                CompatNetPayload.Write(sync.Serialize));
        }
    }

    private static bool AreEqual(CommandPlayerStateSnapshot a, CommandPlayerStateSnapshot b) {
        return a.BodyPosition == b.BodyPosition
            && a.BodyVelocity == b.BodyVelocity
            && a.BodyRotation == b.BodyRotation
            && a.SpawnPosition == b.SpawnPosition
            && a.Health == b.Health
            && a.Food == b.Food
            && a.Sleep == b.Sleep
            && a.Stamina == b.Stamina
            && a.Wetness == b.Wetness
            && a.Temperature == b.Temperature
            && a.AttackPower == b.AttackPower
            && a.AttackResilience == b.AttackResilience
            && a.FallResilience == b.FallResilience
            && a.FireResilience == b.FireResilience
            && a.WalkSpeed == b.WalkSpeed
            && a.JumpSpeed == b.JumpSpeed
            && a.LadderSpeed == b.LadderSpeed
            && a.SwimSpeed == b.SwimSpeed
            && a.FlySpeed == b.FlySpeed
            && a.CreativeFlySpeed == b.CreativeFlySpeed
            && a.Air == b.Air
            && a.AirCapacity == b.AirCapacity
            && a.IsInvulnerable == b.IsInvulnerable;
    }

    private static bool IsValidPlayerState(CommandPlayerStateSync state) {
        return IsFinite(state.SpawnPosition.X)
            && IsFinite(state.SpawnPosition.Y)
            && IsFinite(state.SpawnPosition.Z)
            && IsFinite(state.Health)
            && IsFinite(state.Food)
            && IsFinite(state.Sleep)
            && IsFinite(state.Stamina)
            && IsFinite(state.Wetness)
            && IsFinite(state.Temperature)
            && IsFinite(state.AttackPower)
            && IsFinite(state.AttackResilience)
            && IsFinite(state.FallResilience)
            && IsFinite(state.FireResilience)
            && IsFinite(state.WalkSpeed)
            && IsFinite(state.JumpSpeed)
            && IsFinite(state.LadderSpeed)
            && IsFinite(state.SwimSpeed)
            && IsFinite(state.FlySpeed)
            && IsFinite(state.CreativeFlySpeed)
            && IsFinite(state.Air)
            && IsFinite(state.AirCapacity);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsWithinEditRange(ComponentPlayer player, Point3 position) {
        if (player.ComponentBody == null) {
            return false;
        }

        Vector3 center = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
        return Vector3.DistanceSquared(player.ComponentBody.Position, center) <= 64f;
    }

    private static object FindSubsystem(string typeName) {
        return GameManager.Project?.m_subsystems?.FirstOrDefault(
            subsystem => subsystem.GetType().FullName == typeName);
    }

    private static Type ResolveLoadedType(string fullName) {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type != null) {
                return type;
            }
        }
        return null;
    }

    private static Type ResolveSubmitResultType() {
        return s_submitResultType ??= ResolveLoadedType(SubmitResultTypeName);
    }

    private static object SetCommandData(object behavior, Point3 position, string line) {
        if (behavior == null) {
            return null;
        }

        MethodInfo method = behavior.GetType().GetMethod(
            "SetCommandData",
            AnyInstance,
            null,
            [typeof(Point3), typeof(string)],
            null);
        return method?.Invoke(behavior, [position, line]);
    }

    private static int ResolveCommandBlockIndex() {
        return ResolveBlockIndex(CommandBlockTypeName, 333);
    }

    private static int ResolveBlockIndex(string typeName, int fallback) {
        Type type = ResolveLoadedType(typeName);
        object value = type?.GetField("Index", AnyStatic)?.GetValue(null);
        return value is int index ? index : fallback;
    }

    private static int SetWorkingMode(int cellValue, object mode) {
        Type type = ResolveLoadedType(CommandBlockTypeName);
        MethodInfo method = type?.GetMethod(
            "SetWorkingMode",
            AnyStatic,
            null,
            [typeof(int), mode?.GetType() ?? typeof(object)],
            null);
        if (method == null && type != null) {
            method = type.GetMethods(AnyStatic).FirstOrDefault(candidate =>
                candidate.Name == "SetWorkingMode"
                && candidate.GetParameters().Length == 2
                && candidate.GetParameters()[0].ParameterType == typeof(int));
        }
        return method?.Invoke(null, [cellValue, mode]) is int value ? value : cellValue;
    }

    private static bool IsElectricOutputMode(object commandData) {
        object mode = ReadField(commandData, "Mode");
        return mode != null && Convert.ToInt32(mode) != 0;
    }

    private static object ReadField(object instance, string name) {
        return instance?.GetType().GetField(name, AnyInstance)?.GetValue(instance);
    }

    private static bool TryReadCommandData(
        object commandData,
        out Point3 position,
        out string line) {
        position = default;
        line = string.Empty;
        if (commandData == null) {
            return false;
        }

        object positionValue = ReadField(commandData, "Position");
        object lineValue = ReadField(commandData, "Line");
        if (positionValue is not Point3 point || lineValue is not string commandLine) {
            return false;
        }
        position = point;
        line = commandLine;
        return IsValidLineLength(line);
    }

    private static object InvokeSubmit(
        object subsystemCommand,
        string name,
        object commandData,
        bool judge) {
        if (subsystemCommand == null || commandData == null) {
            return CreateSubmitResult(1);
        }

        MethodInfo method = ResolveSubmitMethod(subsystemCommand.GetType(), commandData.GetType());
        if (method == null) {
            Log.Error("[Survivalcraft.CompatNet] Cannot find Command.SubsystemCommand.Submit.");
            return CreateSubmitResult(1);
        }

        try {
            return method.Invoke(subsystemCommand, [name, commandData, judge]) ?? CreateSubmitResult(1);
        }
        catch (TargetInvocationException e) {
            Log.Error($"[Survivalcraft.CompatNet] Command.Submit failed: {e.InnerException ?? e}");
            return CreateSubmitResult(6);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Command.Submit failed: {e}");
            return CreateSubmitResult(6);
        }
    }

    private static MethodInfo ResolveSubmitMethod(Type type, Type commandDataType) {
        if (s_submitMethod != null && s_submitMethod.DeclaringType.IsAssignableFrom(type)) {
            return s_submitMethod;
        }
        return s_submitMethod = type.GetMethods(AnyInstance).FirstOrDefault(method =>
            method.Name == "Submit"
            && method.ReturnType.FullName == SubmitResultTypeName
            && method.GetParameters().Length == 3
            && method.GetParameters()[0].ParameterType == typeof(string)
            && method.GetParameters()[1].ParameterType.IsAssignableFrom(commandDataType)
            && method.GetParameters()[2].ParameterType == typeof(bool));
    }

    private static object CreateSubmitResult(int value) {
        Type type = ResolveSubmitResultType();
        return type != null && type.IsEnum ? Enum.ToObject(type, value) : null;
    }

    private static int GetSubmitResultValue(object result) {
        return result == null ? 1 : Convert.ToInt32(result);
    }

    private static FieldInfo ResolveElectricVoltageField(Type type) {
        if (s_commandElectricVoltageField != null && s_commandElectricVoltageField.DeclaringType.IsAssignableFrom(type)) {
            return s_commandElectricVoltageField;
        }
        return s_commandElectricVoltageField = type.GetField("m_voltage", AnyInstance);
    }
}
