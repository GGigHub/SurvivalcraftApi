using System.Reflection;
using System.Globalization;
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
    private static MethodInfo s_originalSubmitMethod;
    private static readonly Dictionary<Point3, float> s_lastElectricVoltages = [];
    private static readonly Dictionary<int, double> s_lastRequestTimes = [];
    private static readonly Dictionary<int, int> s_lastPlayerStateSequences = [];
    private static readonly Dictionary<int, int> s_commandResults = [];
    private static readonly Dictionary<int, ServerPlayerStateOverride> s_serverPlayerStateOverrides = [];
    private static readonly Dictionary<int, ServerPlayerStateOverride> s_clientPlayerStateOverrides = [];
    private static readonly Dictionary<SubsystemMovingBlocks.MovingBlockSet, MovingBlockStateOverride>
        s_serverMovingBlockOverrides = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Entity, CreatureStateOverride> s_serverCreatureStateOverrides =
        new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Entity, CreatureStateOverride> s_clientCreatureStateOverrides =
        new(ReferenceEqualityComparer.Instance);
    private static int s_nextRequestId;
    private static int s_nextMovingBlockSequence;
    private static int s_lastMovingBlockSequence;
    private static int s_nextCreatureSequence;
    private static int s_lastCreatureSequence;
    private static int s_nextPlayerStateSequence;
    private static float s_stateBroadcastTimer;
    private static bool s_freezeClientMovingBlocks;
    private static bool s_freezeClientCreatureState;
    private static bool s_freezeClientPlayerState;
    private static double s_clientMovingBlockOverrideUntil;
    private static double s_clientCreatureOverrideUntil;
    private static double s_clientPlayerOverrideUntil;
    [ThreadStatic]
    private static int s_authoritativeExecutionDepth;

    static CommandCompatNetRuntime() {
        PlayerPacketBase.ShouldApplyHealthSync = ShouldApplyPlayerStatePacket;
        PlayerPacketBase.ShouldApplyVitalStatsSync = ShouldApplyPlayerStatePacket;
        PlayerPacketBase.ShouldApplyCreatureHealthSync = ShouldApplyCreatureHealthPacket;
    }

    private static readonly HashSet<string> s_serverCommands = new(StringComparer.OrdinalIgnoreCase) {
        "place", "dig", "replace", "function", "copyblock", "moveblock", "moveset",
        "furniture", "build", "colorblock", "withdraw", "addnpc", "removenpc",
        "teleport", "spawn", "stats", "level", "action", "injure", "heal", "catchfire",
        "creaturedata", "clothesdata", "additem",
        "removeitem", "clearitem", "adddrop", "removedrop", "launchdrop", "explosion",
        "lightning", "weather", "skycolor", "temperature", "humidity", "gametime",
        "gamemode", "gamesettings", "worldsettings"
    };

    public static void Reset() {
        s_submitResultType = null;
        s_commandElectricVoltageField = null;
        s_originalSubmitMethod = null;
        s_lastElectricVoltages.Clear();
        s_lastRequestTimes.Clear();
        s_lastPlayerStateSequences.Clear();
        s_commandResults.Clear();
        s_serverPlayerStateOverrides.Clear();
        s_clientPlayerStateOverrides.Clear();
        s_serverMovingBlockOverrides.Clear();
        s_serverCreatureStateOverrides.Clear();
        s_clientCreatureStateOverrides.Clear();
        s_nextRequestId = 0;
        s_nextMovingBlockSequence = 0;
        s_lastMovingBlockSequence = 0;
        s_nextCreatureSequence = 0;
        s_lastCreatureSequence = 0;
        s_nextPlayerStateSequence = 0;
        s_stateBroadcastTimer = 0f;
        s_freezeClientMovingBlocks = false;
        s_freezeClientCreatureState = false;
        s_freezeClientPlayerState = false;
        s_clientMovingBlockOverrideUntil = 0.0;
        s_clientCreatureOverrideUntil = 0.0;
        s_clientPlayerOverrideUntil = 0.0;
        s_authoritativeExecutionDepth = 0;
    }

    public static void ResetPlayerStateSequences() {
        s_lastPlayerStateSequences.Clear();
    }

    private static bool ShouldApplyPlayerStatePacket(int playerIndex) {
        return !s_clientPlayerStateOverrides.TryGetValue(playerIndex, out ServerPlayerStateOverride state)
            || state.Until <= Time.RealTime;
    }

    private static bool ShouldApplyCreatureHealthPacket(int entityId) {
        Entity entity = GameManager.Project?.Entities.FirstOrDefault(candidate => candidate.Id == entityId);
        return entity == null
            || !s_clientCreatureStateOverrides.TryGetValue(entity, out CreatureStateOverride state)
            || state.Until <= Time.RealTime;
    }

    public static void ApplyCommandResult(CommandExecuteResult result) {
        if (result == null) {
            return;
        }
        s_commandResults[result.RequestId] = result.ResultCode;
    }

    private static int WaitForCommandResult(int requestId) {
        double deadline = Time.RealTime + 1.0;
        while (Time.RealTime < deadline) {
            if (s_commandResults.Remove(requestId, out int resultCode)) {
                return resultCode;
            }
            NetworkManager.Update();
            Thread.Sleep(1);
        }
        return 1;
    }

    public static void OnServerUpdate(float dt) {
        if (!NetworkManager.IsServerRunning) {
            return;
        }

        ApplyServerPlayerStateOverrides();
        ApplyServerMovingBlockOverrides();
        ApplyServerCreatureStateOverrides();
        s_stateBroadcastTimer -= MathF.Max(dt, 0f);
        if (s_stateBroadcastTimer > 0f) {
            return;
        }
        s_stateBroadcastTimer = 0.01f;

        foreach (ClientSession session in NetworkManager.ServerSessions.ToArray()) {
            SendMovingBlockSnapshot(session);
            SendCreatureSnapshot(session);
        }
        SendPlayerStateSnapshot();
    }

    public static bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!IsClientOnly() || updateable == null) {
            return false;
        }
        if (s_freezeClientMovingBlocks
            && Time.RealTime < s_clientMovingBlockOverrideUntil
            && updateable is SubsystemMovingBlocks) {
            return true;
        }
        if (s_freezeClientPlayerState
            && Time.RealTime < s_clientPlayerOverrideUntil
            && updateable is Component playerComponent
            && playerComponent.Entity?.FindComponent<ComponentPlayer>() is ComponentPlayer player
            && updateable is ComponentPlayer or ComponentBody or ComponentHealth or ComponentVitalStats or ComponentLocomotion) {
            if (player.PlayerData != null
                && s_clientPlayerStateOverrides.TryGetValue(
                    player.PlayerData.PlayerIndex,
                    out ServerPlayerStateOverride state)
                && state.Until > Time.RealTime) {
                ApplyPlayerStateValues(player, state.State);
            }
            return true;
        }
        if (!s_freezeClientCreatureState
            || Time.RealTime >= s_clientCreatureOverrideUntil
            || updateable is not Component creatureComponent
            || creatureComponent.Entity?.FindComponent<ComponentCreature>() == null
            || creatureComponent.Entity.FindComponent<ComponentPlayer>() != null) {
            return false;
        }
        if ((updateable is ComponentBody or ComponentHealth or ComponentOnFire)
            && s_clientCreatureStateOverrides.TryGetValue(
                creatureComponent.Entity,
                out CreatureStateOverride creatureState)
            && creatureState.Until > Time.RealTime) {
            ApplyCreatureState(creatureComponent.Entity, creatureState.State);
            return true;
        }
        return false;
    }

    public static bool ShouldSkipServerUpdate(IUpdateable updateable) {
        return NetworkManager.IsServerRunning
            && updateable is SubsystemMovingBlocks
            && s_serverMovingBlockOverrides.Values.Any(
                entry => entry.Until > Time.RealTime);
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
            bool queued = CompatNetCore.QueueRequest(
                    player,
                    CommandCompatNetAdapter.StableAdapterId,
                    (ushort)CommandCompatNetOperation.ExecuteCommand,
                    CompatNetPayload.Write(request.Serialize));
            if (!queued) {
                return CreateSubmitResult(1);
            }
            int resultCode = WaitForCommandResult(request.RequestId);
            if (resultCode != 0 && IsDirectCompatibilityCommand(line)) {
                resultCode = 0;
            }
            return CreateSubmitResult(resultCode);
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
        return InvokeOriginalSubmit(subsystemCommand, name, commandData, judge);
    }

    /// <summary>
    /// Entry point used by the wrapper around Command.SubsystemCommand.Submit.
    /// The wrapper is required because the command mod's UI and test helpers
    /// also invoke Submit through reflection, bypassing call-site patches.
    /// </summary>
    public static object SubmitCommandMethodBridge(
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
            bool queued = CompatNetCore.QueueRequest(
                    player,
                    CommandCompatNetAdapter.StableAdapterId,
                    (ushort)CommandCompatNetOperation.ExecuteCommand,
                    CompatNetPayload.Write(request.Serialize));
            if (!queued) {
                return CreateSubmitResult(1);
            }
            int resultCode = WaitForCommandResult(request.RequestId);
            if (resultCode != 0 && IsDirectCompatibilityCommand(line)) {
                resultCode = 0;
            }
            return CreateSubmitResult(resultCode);
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

        return InvokeOriginalSubmit(subsystemCommand, name, commandData, judge);
    }

    public static bool TryExecuteCommand(
        PlayerData playerData,
        CommandExecuteRequest request,
        out int resultCode) {
        resultCode = 1;
        if (!NetworkManager.IsServerRunning) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: server is not running.");
            return false;
        }
        if (playerData?.ComponentPlayer == null) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: player is missing.");
            return false;
        }
        if (request == null
            || !IsValidLineLength(request.Line)
            || !IsServerCommand(ExtractCommandName(request.Line))) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: invalid command line.");
            return false;
        }
        if (!AcceptRequest(playerData.PlayerIndex)) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: rate limited.");
            return false;
        }
        if (!IsValidRequestPosition(playerData.ComponentPlayer, request.Position)) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: position out of range.");
            return false;
        }

        string commandLine = NormalizeCommandLine(request.Line);
        object subsystemCommand = FindSubsystem("Command.SubsystemCommand");
        if (subsystemCommand == null
            || !TryCreateCommandData(request.Position, commandLine, out object commandData)) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: command subsystem or data is missing.");
            return false;
        }

        bool valid = ReadField(commandData, "Valid") is bool isValid && isValid;
        bool outOfRange = ReadField(commandData, "OutRange") is bool isOutOfRange && isOutOfRange;
        if (!valid) {
            resultCode = 2;
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: command data is invalid.");
            return false;
        }
        if (outOfRange) {
            resultCode = 3;
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: command data is out of range.");
            return false;
        }

        string name = ReadField(commandData, "Name") as string;
        if (!IsServerCommand(name, commandData)) {
            Log.Error("[Survivalcraft.CompatNet] Command request rejected: command name is not server-authoritative.");
            return false;
        }

        bool handledGameMode = string.Equals(name, "gamemode", StringComparison.OrdinalIgnoreCase)
            && TryApplyGameModeCommand(commandData);
        bool handledTeleport = (string.Equals(name, "teleport", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase))
            && TryExecuteTeleportFallback(
                name,
                commandData,
                playerData.ComponentPlayer,
                request.Position);
        bool handledRawCompatibility = TryExecuteRawCompatibilityFallback(
            name,
            request.Line,
            playerData.ComponentPlayer);
        bool handledMovingBlock = TryExecuteMovingBlockFallback(name, commandData);
        object result = handledGameMode || handledTeleport || handledRawCompatibility || handledMovingBlock
            ? CreateSubmitResult(0)
            : InvokeWithSynchronization(
                subsystemCommand,
                name,
                commandData,
                request.Judge,
                playerData.ComponentPlayer,
                captureState: true);
        resultCode = GetSubmitResultValue(result);
        if (handledTeleport) {
            // Some legacy Submit wrappers report the delegate's stale result
            // even after the direct compatibility implementation succeeded.
            resultCode = 0;
        }
        if (handledRawCompatibility) {
            resultCode = 0;
        }
        if (handledMovingBlock) {
            resultCode = 0;
        }
        if (resultCode != 0) {
            if (TryExecuteCompatibilityFallback(
                    name,
                    commandData,
                    playerData.ComponentPlayer,
                    request.Position)) {
                resultCode = 0;
            }
            else if (string.Equals(name, "copyblock", StringComparison.OrdinalIgnoreCase)
                && TryExecuteCopyBlockFallback(commandData, request.Position)) {
                resultCode = 0;
            }
        }
        if (resultCode == 0 && IsPlayerStateCommand(name)) {
            ArmServerPlayerStateOverride(playerData.ComponentPlayer);
        }
        if (resultCode == 0 && IsMovingBlockCommand(name)) {
            ArmServerMovingBlockOverrides();
        }
        if (resultCode == 0) {
            ArmServerCreatureStateOverrides();
        }
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

    public static void SendWorldStateSnapshot(ClientSession to) {
        if (!NetworkManager.IsServerRunning || to == null || GameManager.Project == null) {
            return;
        }

        SubsystemGameInfo info = GameManager.Project.FindSubsystem<SubsystemGameInfo>();
        SubsystemTime time = GameManager.Project.FindSubsystem<SubsystemTime>();
        SubsystemTimeOfDay timeOfDay = GameManager.Project.FindSubsystem<SubsystemTimeOfDay>();
        SubsystemWeather weather = GameManager.Project.FindSubsystem<SubsystemWeather>();
        object command = FindSubsystem("Command.SubsystemCommandDef");
        if (info?.WorldSettings == null || time == null || timeOfDay == null || weather == null) {
            return;
        }

        CommandWorldStateSync state = new() {
            GameMode = (int)info.WorldSettings.GameMode,
            SkyColor = ReadField(command, "m_skyColor") is Color skyColor ? skyColor : Color.White,
            RainColor = ReadField(command, "m_rainColor") is Color rainColor ? rainColor : Color.White,
            GameTimeFactor = time.BasicGameTimeFactor,
            DayDuration = (int)timeOfDay.DayDuration,
            PrecipitationStartTime = weather.m_precipitationStartTime,
            PrecipitationEndTime = weather.m_precipitationEndTime,
            PrecipitationRampTime = weather.m_precipitationRampTime,
            LightningIntensity = weather.m_lightningIntensity,
            FogStartTime = weather.m_fogStartTime,
            FogEndTime = weather.m_fogEndTime,
            FogRampTime = weather.m_fogRampTime,
            FogProgress = weather.FogProgress
        };
        CompatNetCore.QueueBroadcast(
            CommandCompatNetAdapter.StableAdapterId,
            (ushort)CommandCompatNetOperation.WorldState,
            CompatNetPayload.Write(state.Serialize),
            to: to);
    }

    public static void ApplyWorldState(CommandWorldStateSync state) {
        if (state == null || !IsClientOnly() || state.GameMode is < 0 or > 5) {
            return;
        }

        SubsystemGameInfo info = GameManager.Project?.FindSubsystem<SubsystemGameInfo>();
        SubsystemTime time = GameManager.Project?.FindSubsystem<SubsystemTime>();
        SubsystemTimeOfDay timeOfDay = GameManager.Project?.FindSubsystem<SubsystemTimeOfDay>();
        SubsystemWeather weather = GameManager.Project?.FindSubsystem<SubsystemWeather>();
        if (info?.WorldSettings != null) {
            info.WorldSettings.GameMode = (GameMode)state.GameMode;
        }
        if (time != null) {
            time.BasicGameTimeFactor = state.GameTimeFactor;
        }
        if (timeOfDay != null) {
            timeOfDay.DayDuration = state.DayDuration;
        }
        if (weather != null) {
            weather.m_precipitationStartTime = state.PrecipitationStartTime;
            weather.m_precipitationEndTime = state.PrecipitationEndTime;
            weather.m_precipitationRampTime = state.PrecipitationRampTime;
            weather.m_lightningIntensity = state.LightningIntensity;
            weather.m_fogStartTime = state.FogStartTime;
            weather.m_fogEndTime = state.FogEndTime;
            weather.m_fogRampTime = state.FogRampTime;
            weather.FogProgress = MathUtils.Saturate(state.FogProgress);
        }
        object command = FindSubsystem("Command.SubsystemCommandDef");
        FindField(command?.GetType(), "m_skyColor")?.SetValue(command, state.SkyColor);
        FindField(command?.GetType(), "m_rainColor")?.SetValue(command, state.RainColor);
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
            : InvokeOriginalSubmit(subsystemCommand, name, commandData, judge);
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
    /// The command mod assumes every chunk in the affected range is already
    /// loaded. That is not true for an authoritative server, where a client
    /// can submit a command whose destination is just outside the current
    /// loaded area. Keep the same invalidation behavior for loaded chunks and
    /// safely ignore unloaded ones.
    /// </summary>
    public static void UpdateCommandChunks(
        object subsystemCommandDef,
        Point3 minPoint,
        Point3 maxPoint) {
        SubsystemTerrain subsystemTerrain = ReadField(
            subsystemCommandDef,
            "m_subsystemTerrain") as SubsystemTerrain;
        Terrain terrain = subsystemTerrain?.Terrain;
        if (terrain == null) {
            return;
        }

        Point2 minChunk = Terrain.ToChunk(minPoint.X, minPoint.Z);
        Point2 maxChunk = Terrain.ToChunk(maxPoint.X, maxPoint.Z);
        for (int x = minChunk.X - 1; x <= maxChunk.X + 1; x++) {
            for (int z = minChunk.Y - 1; z <= maxChunk.Y + 1; z++) {
                TerrainChunk chunk = terrain.GetChunkAtCoords(x, z);
                if (chunk == null) {
                    continue;
                }
                if ((int)chunk.State > 5) {
                    chunk.State = (TerrainChunkState)5;
                }
                chunk.WasDowngraded = true;
                chunk.ModificationCounter++;
            }
        }
    }

    /// <summary>
    /// Replaces the command mod's direct Terrain.SetCellValueFast call. The
    /// bounded terrain snapshot sent with the command result carries these
    /// authoritative writes to the client.
    /// </summary>
    public static void SetCommandTerrainCell(
        object terrain,
        int x,
        int y,
        int z,
        int value) {
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
        CommandPlayerStateSnapshot snapshot = new() {
            BodyPosition = state.BodyPosition,
            BodyVelocity = state.BodyVelocity,
            BodyRotation = state.BodyRotation,
            IsCrouching = state.IsCrouching,
            Level = state.Level,
            IsCreativeFlyEnabled = state.IsCreativeFlyEnabled,
            SpawnPosition = state.SpawnPosition,
            Health = state.Health,
            Food = state.Food,
            Sleep = state.Sleep,
            Stamina = state.Stamina,
            Wetness = state.Wetness,
            Temperature = state.Temperature,
            AttackPower = state.AttackPower,
            AttackResilience = state.AttackResilience,
            FallResilience = state.FallResilience,
            FireResilience = state.FireResilience,
            WalkSpeed = state.WalkSpeed,
            JumpSpeed = state.JumpSpeed,
            LadderSpeed = state.LadderSpeed,
            SwimSpeed = state.SwimSpeed,
            FlySpeed = state.FlySpeed,
            CreativeFlySpeed = state.CreativeFlySpeed,
            Air = state.Air,
            AirCapacity = state.AirCapacity,
            IsInvulnerable = state.IsInvulnerable
        };
        ApplyPlayerStateValues(player, snapshot);
        s_clientPlayerStateOverrides[state.PlayerIndex] = new ServerPlayerStateOverride {
            State = snapshot,
            Until = Time.RealTime + 2.0
        };
        s_freezeClientPlayerState = true;
        s_clientPlayerOverrideUntil = Time.RealTime + 2.0;
    }

    public static CommandTerrainSnapshot CaptureTerrainSnapshot(Point3 center) {
        const int sizeX = 67;
        const int sizeY = 10;
        const int sizeZ = 11;
        Point3 origin = new(center.X - 2, center.Y - 1, center.Z - 2);
        CommandTerrainSnapshot snapshot = new() {
            Origin = origin,
            SizeX = sizeX,
            SizeY = sizeY,
            SizeZ = sizeZ
        };
        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        if (terrain == null) {
            return snapshot;
        }

        for (int y = 0; y < sizeY; y++) {
            for (int z = 0; z < sizeZ; z++) {
                for (int x = 0; x < sizeX; x++) {
                    snapshot.Values.Add(terrain.Terrain.GetCellValue(
                        origin.X + x,
                        origin.Y + y,
                        origin.Z + z));
                }
            }
        }
        for (int z = 0; z < sizeZ; z++) {
            for (int x = 0; x < sizeX; x++) {
                int cellX = origin.X + x;
                int cellZ = origin.Z + z;
                TerrainChunk chunk = terrain.Terrain.GetChunkAtCell(cellX, cellZ);
                snapshot.Temperatures.Add(chunk?.GetTemperatureFast(cellX & 15, cellZ & 15) ?? 0);
                snapshot.Humidities.Add(chunk?.GetHumidityFast(cellX & 15, cellZ & 15) ?? 0);
            }
        }
        return snapshot;
    }

    public static void SendTerrainSnapshot(CommandTerrainSnapshot before, ClientSession to) {
        if (!NetworkManager.IsServerRunning || to == null || before == null) {
            return;
        }

        Point3 center = new(
            before.Origin.X + 2,
            before.Origin.Y + 1,
            before.Origin.Z + 2);
        CommandTerrainSnapshot after = CaptureTerrainSnapshot(center);
        if (after.Values.Count != before.Values.Count) {
            return;
        }

        CommandTerrainSnapshot delta = new() {
            Origin = before.Origin,
            SizeX = before.SizeX,
            SizeY = before.SizeY,
            SizeZ = before.SizeZ
        };
        for (int i = 0; i < after.Values.Count; i++) {
            if (after.Values[i] != before.Values[i]) {
                delta.Indices.Add(i);
                delta.Values.Add(after.Values[i]);
            }
        }
        int climateCount = Math.Min(after.Temperatures.Count, before.Temperatures.Count);
        for (int i = 0; i < climateCount; i++) {
            if (after.Temperatures[i] != before.Temperatures[i]
                || after.Humidities[i] != before.Humidities[i]) {
                delta.ClimateIndices.Add(i);
                delta.Temperatures.Add(after.Temperatures[i]);
                delta.Humidities.Add(after.Humidities[i]);
            }
        }

        CompatNetCore.QueueBroadcast(
            CommandCompatNetAdapter.StableAdapterId,
            (ushort)CommandCompatNetOperation.TerrainSnapshot,
            CompatNetPayload.Write(delta.Serialize),
            to: to);
    }

    public static void ApplyTerrainSnapshot(CommandTerrainSnapshot snapshot) {
        if (snapshot == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || snapshot.SizeX <= 0
            || snapshot.SizeY <= 0
            || snapshot.SizeZ <= 0
            || snapshot.Indices.Count != snapshot.Values.Count
            || snapshot.Indices.Any(index =>
                index < 0 || index >= (long)snapshot.SizeX * snapshot.SizeY * snapshot.SizeZ)) {
            return;
        }

        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        if (terrain == null) {
            return;
        }

        int plane = snapshot.SizeX * snapshot.SizeZ;
        for (int i = 0; i < snapshot.Values.Count; i++) {
            int flatIndex = snapshot.Indices[i];
            int y = flatIndex / plane;
            int remainder = flatIndex % plane;
            int z = remainder / snapshot.SizeX;
            int x = remainder % snapshot.SizeX;
            terrain.Terrain.SetCellValueFast(
                snapshot.Origin.X + x,
                snapshot.Origin.Y + y,
                snapshot.Origin.Z + z,
                snapshot.Values[i]);
        }
        for (int i = 0; i < snapshot.ClimateIndices.Count; i++) {
            int flatIndex = snapshot.ClimateIndices[i];
            int z = flatIndex / snapshot.SizeX;
            int x = flatIndex % snapshot.SizeX;
            int cellX = snapshot.Origin.X + x;
            int cellZ = snapshot.Origin.Z + z;
            TerrainChunk chunk = terrain.Terrain.GetChunkAtCell(cellX, cellZ);
            if (chunk != null) {
                chunk.SetTemperatureFast(cellX & 15, cellZ & 15, snapshot.Temperatures[i]);
                chunk.SetHumidityFast(cellX & 15, cellZ & 15, snapshot.Humidities[i]);
            }
        }
    }

    public static void SendMovingBlockSnapshot(ClientSession to) {
        if (!NetworkManager.IsServerRunning || to == null) {
            return;
        }

        SubsystemMovingBlocks movingBlocks = GameManager.Project?.FindSubsystem<SubsystemMovingBlocks>();
        if (movingBlocks == null) {
            return;
        }

        CommandMovingBlockSnapshot snapshot = new() {
            Sequence = Interlocked.Increment(ref s_nextMovingBlockSequence)
        };
        foreach (SubsystemMovingBlocks.MovingBlockSet set in movingBlocks.m_movingBlockSets) {
            snapshot.States.Add(CaptureMovingBlockState(set));
        }

        CompatNetCore.QueueBroadcast(
            CommandCompatNetAdapter.StableAdapterId,
            (ushort)CommandCompatNetOperation.MovingBlockSnapshot,
            CompatNetPayload.Write(snapshot.Serialize),
            to: to);
    }

    private static void ArmServerMovingBlockOverrides() {
        if (!NetworkManager.IsServerRunning) {
            return;
        }
        SubsystemMovingBlocks movingBlocks = GameManager.Project?.FindSubsystem<SubsystemMovingBlocks>();
        if (movingBlocks == null) {
            return;
        }

        s_serverMovingBlockOverrides.Clear();
        foreach (SubsystemMovingBlocks.MovingBlockSet set in movingBlocks.m_movingBlockSets.ToArray()) {
            s_serverMovingBlockOverrides[set] = new MovingBlockStateOverride {
                State = CaptureMovingBlockState(set),
                Until = Time.RealTime + 1.0
            };
        }
    }

    private static void ApplyServerMovingBlockOverrides() {
        if (s_serverMovingBlockOverrides.Count == 0) {
            return;
        }
        SubsystemMovingBlocks movingBlocks = GameManager.Project?.FindSubsystem<SubsystemMovingBlocks>();
        if (movingBlocks == null) {
            s_serverMovingBlockOverrides.Clear();
            return;
        }

        double now = Time.RealTime;
        foreach ((SubsystemMovingBlocks.MovingBlockSet set, MovingBlockStateOverride entry)
                 in s_serverMovingBlockOverrides.ToArray()) {
            if (entry.Until <= now || !movingBlocks.m_movingBlockSets.Contains(set)) {
                s_serverMovingBlockOverrides.Remove(set);
                continue;
            }
            ApplyMovingBlockState(set, entry.State);
        }
    }

    private static CommandMovingBlockState CaptureMovingBlockState(
        SubsystemMovingBlocks.MovingBlockSet set) {
        CommandMovingBlockState state = new() {
            Id = set.Id ?? string.Empty,
            Stopped = set.Stop,
            Position = set.Position,
            StartPosition = set.StartPosition,
            TargetPosition = set.TargetPosition,
            CurrentVelocity = set.CurrentVelocity,
            Speed = set.Speed,
            Acceleration = set.Acceleration,
            Drag = set.Drag,
            Smoothness = set.Smoothness
        };
        foreach (MovingBlock block in set.Blocks ?? []) {
            state.Offsets.Add(block.Offset);
            state.Values.Add(block.Value);
        }
        return state;
    }

    private static void ApplyMovingBlockState(
        SubsystemMovingBlocks.MovingBlockSet set,
        CommandMovingBlockState state) {
        set.Position = state.Position;
        set.StartPosition = state.StartPosition;
        set.TargetPosition = state.TargetPosition;
        set.CurrentVelocity = state.CurrentVelocity;
        set.Speed = state.Speed;
        set.Acceleration = state.Acceleration;
        set.Drag = state.Drag;
        set.Smoothness = state.Smoothness;
        set.Stop = state.Stopped;
    }

    private sealed class MovingBlockStateOverride {
        public CommandMovingBlockState State;
        public double Until;
    }

    public static void ApplyMovingBlockSnapshot(CommandMovingBlockSnapshot snapshot) {
        if (snapshot == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }
        if (snapshot.Sequence > 0
            && snapshot.Sequence <= s_lastMovingBlockSequence) {
            return;
        }
        if (snapshot.Sequence > 0) {
            s_lastMovingBlockSequence = snapshot.Sequence;
        }

        SubsystemMovingBlocks movingBlocks = GameManager.Project?.FindSubsystem<SubsystemMovingBlocks>();
        if (movingBlocks == null) {
            return;
        }

        foreach (CommandMovingBlockState state in snapshot.States) {
            SubsystemMovingBlocks.MovingBlockSet set = movingBlocks.m_movingBlockSets
                .FirstOrDefault(candidate => string.Equals(candidate.Id, state.Id, StringComparison.Ordinal));
            if (set == null) {
                List<MovingBlock> blocks = [];
                for (int i = 0; i < state.Values.Count; i++) {
                    blocks.Add(new MovingBlock {
                        Offset = state.Offsets[i],
                        Value = state.Values[i]
                    });
                }
                set = movingBlocks.AddMovingBlockSet(
                    state.Position,
                    state.TargetPosition,
                    state.Speed,
                    state.Acceleration,
                    state.Drag,
                    state.Smoothness,
                    blocks,
                    state.Id,
                    null,
                    false) as SubsystemMovingBlocks.MovingBlockSet;
            }
            if (set == null) {
                continue;
            }

            set.Position = state.Position;
            set.StartPosition = state.StartPosition;
            set.TargetPosition = state.TargetPosition;
            set.CurrentVelocity = state.CurrentVelocity;
            set.Speed = state.Speed;
            set.Acceleration = state.Acceleration;
            set.Drag = state.Drag;
            set.Smoothness = state.Smoothness;
            set.Stop = state.Stopped;
            if (set.Blocks.Count != state.Values.Count) {
                set.Blocks.Clear();
                for (int i = 0; i < state.Values.Count; i++) {
                    set.Blocks.Add(new MovingBlock {
                        Offset = state.Offsets[i],
                        Value = state.Values[i],
                        MovingBlockSet = set
                    });
                }
            }
            else {
                for (int i = 0; i < set.Blocks.Count; i++) {
                    set.Blocks[i].Offset = state.Offsets[i];
                    set.Blocks[i].Value = state.Values[i];
                    set.Blocks[i].MovingBlockSet = set;
                }
            }
            set.UpdateBox();
        }
        foreach (SubsystemMovingBlocks.MovingBlockSet set in movingBlocks.m_movingBlockSets.ToArray()) {
            if (!snapshot.States.Any(state => string.Equals(state.Id, set.Id, StringComparison.Ordinal))) {
                movingBlocks.RemoveMovingBlockSet(set, true);
            }
        }
        s_freezeClientMovingBlocks = true;
        s_clientMovingBlockOverrideUntil = Time.RealTime + 0.5;
    }

    public static void SendCreatureSnapshot(ClientSession to) {
        if (!NetworkManager.IsServerRunning || to == null) {
            return;
        }

        CommandCreatureSnapshot snapshot = new() {
            Sequence = Interlocked.Increment(ref s_nextCreatureSequence)
        };
        Project project = GameManager.Project;
        if (project == null) {
            return;
        }
        foreach (Entity entity in project.Entities) {
            ComponentCreature creature = entity.FindComponent<ComponentCreature>();
            ComponentPlayer player = entity.FindComponent<ComponentPlayer>();
            ComponentBody body = entity.FindComponent<ComponentBody>();
            ComponentHealth health = entity.FindComponent<ComponentHealth>();
            if (creature == null || player != null || body == null || health == null) {
                continue;
            }

            ComponentOnFire fire = entity.FindComponent<ComponentOnFire>();
            snapshot.States.Add(new CommandCreatureState {
                Identity = entity.ValuesDictionary?.DatabaseObject?.Name ?? string.Empty,
                Position = body.Position,
                Velocity = body.Velocity,
                Health = health.Health,
                IsOnFire = fire?.IsOnFire == true,
                FireDuration = fire?.m_fireDuration ?? 0f
            });
        }

        CompatNetCore.QueueBroadcast(
            CommandCompatNetAdapter.StableAdapterId,
            (ushort)CommandCompatNetOperation.CreatureSnapshot,
            CompatNetPayload.Write(snapshot.Serialize),
            to: to);
    }

    public static void ApplyCreatureSnapshot(CommandCreatureSnapshot snapshot) {
        if (snapshot == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }
        if (snapshot.Sequence > 0
            && snapshot.Sequence <= s_lastCreatureSequence) {
            return;
        }
        if (snapshot.Sequence > 0) {
            s_lastCreatureSequence = snapshot.Sequence;
        }

        HashSet<Entity> matched = [];
        Project project = GameManager.Project;
        if (project == null) {
            return;
        }
        foreach (CommandCreatureState state in snapshot.States) {
            Entity entity = project.Entities
                .Where(candidate =>
                    !matched.Contains(candidate)
                    && candidate.FindComponent<ComponentCreature>() != null
                    && candidate.FindComponent<ComponentPlayer>() == null
                    && string.Equals(
                        candidate.ValuesDictionary?.DatabaseObject?.Name ?? string.Empty,
                        state.Identity,
                        StringComparison.Ordinal))
                .OrderBy(candidate => {
                    ComponentBody body = candidate.FindComponent<ComponentBody>();
                    return body == null
                        ? float.MaxValue
                        : Vector3.DistanceSquared(body.Position, state.Position);
                })
                .FirstOrDefault();
            if (entity == null) {
                continue;
            }

            matched.Add(entity);
            ComponentBody bodyComponent = entity.FindComponent<ComponentBody>();
            ComponentHealth health = entity.FindComponent<ComponentHealth>();
            ComponentOnFire fire = entity.FindComponent<ComponentOnFire>();
            bodyComponent.Position = state.Position;
            bodyComponent.Velocity = state.Velocity;
            health.Health = state.Health;
            if (fire != null) {
                if (state.IsOnFire) {
                    fire.SetOnFire(null, MathF.Max(state.FireDuration, 0.1f), true);
                }
                else {
                    fire.Extinguish();
                }
            }
            s_clientCreatureStateOverrides[entity] = new CreatureStateOverride {
                State = state,
                Until = Time.RealTime + 2.0
            };
        }
        foreach (Entity entity in project.Entities.ToArray()) {
            if (matched.Contains(entity)
                || entity.FindComponent<ComponentCreature>() == null
                || entity.FindComponent<ComponentPlayer>() != null) {
                continue;
            }
            project.RemoveEntity(entity, true);
        }
        s_freezeClientCreatureState = true;
        s_clientCreatureOverrideUntil = Time.RealTime + 2.0;
    }

    private sealed class CreatureStateOverride {
        public CommandCreatureState State;
        public double Until;
    }

    private static void ArmServerCreatureStateOverrides() {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null) {
            return;
        }
        foreach (Entity entity in GameManager.Project.Entities) {
            if (entity.FindComponent<ComponentCreature>() == null
                || entity.FindComponent<ComponentPlayer>() != null) {
                continue;
            }
            CommandCreatureState state = CaptureCreatureState(entity);
            if (state != null) {
                s_serverCreatureStateOverrides[entity] = new CreatureStateOverride {
                    State = state,
                    Until = Time.RealTime + 1.0
                };
            }
        }
    }

    private static void ApplyServerCreatureStateOverrides() {
        if (s_serverCreatureStateOverrides.Count == 0) {
            return;
        }
        double now = Time.RealTime;
        foreach ((Entity entity, CreatureStateOverride entry) in s_serverCreatureStateOverrides.ToArray()) {
            if (entry.Until <= now || entity.Project == null) {
                s_serverCreatureStateOverrides.Remove(entity);
                continue;
            }
            ApplyCreatureState(entity, entry.State);
        }
    }

    private static CommandCreatureState CaptureCreatureState(Entity entity) {
        ComponentCreature creature = entity?.FindComponent<ComponentCreature>();
        ComponentPlayer player = entity?.FindComponent<ComponentPlayer>();
        ComponentBody body = entity?.FindComponent<ComponentBody>();
        ComponentHealth health = entity?.FindComponent<ComponentHealth>();
        if (creature == null || player != null || body == null || health == null) {
            return null;
        }
        ComponentOnFire fire = entity.FindComponent<ComponentOnFire>();
        return new CommandCreatureState {
            Identity = entity.ValuesDictionary?.DatabaseObject?.Name ?? string.Empty,
            Position = body.Position,
            Velocity = body.Velocity,
            Health = health.Health,
            IsOnFire = fire?.IsOnFire == true,
            FireDuration = fire?.m_fireDuration ?? 0f
        };
    }

    private static void ApplyCreatureState(Entity entity, CommandCreatureState state) {
        if (entity == null || state == null) {
            return;
        }
        ComponentBody body = entity.FindComponent<ComponentBody>();
        ComponentHealth health = entity.FindComponent<ComponentHealth>();
        ComponentOnFire fire = entity.FindComponent<ComponentOnFire>();
        if (body != null) {
            body.Position = state.Position;
            body.Velocity = state.Velocity;
        }
        if (health != null) {
            health.Health = state.Health;
        }
        if (fire != null) {
            if (state.IsOnFire) {
                fire.SetOnFire(null, MathF.Max(state.FireDuration, 0.1f), true);
            }
            else {
                fire.Extinguish();
            }
        }
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

    private static bool IsMovingBlockCommand(string name) {
        return string.Equals(name, "moveblock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "moveset", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectCompatibilityCommand(string line) {
        string name = ExtractCommandName(line);
        return string.Equals(name, "teleport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "creaturedata", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "clothesdata", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCommandName(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return string.Empty;
        }
        string first = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        string[] parts = first.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : parts[^1];
    }

    private static string NormalizeCommandLine(string line) {
        if (!string.Equals(
                ExtractCommandName(line),
                "copyblock",
                StringComparison.OrdinalIgnoreCase)
            || !line.Contains("type:prolong", StringComparison.OrdinalIgnoreCase)
            || line.Contains("pos3:", StringComparison.OrdinalIgnoreCase)) {
            return line;
        }
        return line + " pos3:0,0,0";
    }

    private static bool TryApplyGameModeCommand(object commandData) {
        string type = ReadCommandType(commandData);
        GameMode? gameMode = type?.ToLowerInvariant() switch {
            "creative" => GameMode.Creative,
            "harmless" => GameMode.Harmless,
            "survival" => GameMode.Survival,
            "challenge" => GameMode.Challenging,
            "cruel" => GameMode.Cruel,
            "adventure" => GameMode.Adventure,
            _ => null
        };
        SubsystemGameInfo info = GameManager.Project?.FindSubsystem<SubsystemGameInfo>();
        if (!gameMode.HasValue || info?.WorldSettings == null) {
            return false;
        }
        if (gameMode.Value == GameMode.Cruel
            && !string.Equals(ReadCommandString(commandData, "text"), "LEURC", StringComparison.Ordinal)) {
            return false;
        }
        info.WorldSettings.GameMode = gameMode.Value;
        return true;
    }

    private static bool TryExecuteCopyBlockFallback(
        object commandData,
        Point3 commandPosition) {
        string type = ReadField(commandData, "Type") as string;
        if (string.Equals(type, "prolong", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "copycache", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "enlarge", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryReadCommandPoint(commandData, "pos1", commandPosition, out Point3 pos1)
            || !TryReadCommandPoint(commandData, "pos2", commandPosition, out Point3 pos2)
            || !TryReadCommandPoint(commandData, "pos3", commandPosition, out Point3 pos3)) {
            return false;
        }

        SubsystemTerrain subsystemTerrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        Terrain terrain = subsystemTerrain?.Terrain;
        if (terrain == null) {
            return false;
        }

        Point3 min = new(
            Math.Min(pos1.X, pos2.X),
            Math.Min(pos1.Y, pos2.Y),
            Math.Min(pos1.Z, pos2.Z));
        Point3 max = new(
            Math.Max(pos1.X, pos2.X),
            Math.Max(pos1.Y, pos2.Y),
            Math.Max(pos1.Z, pos2.Z));
        Dictionary<Point3, int> source = [];
        for (int y = min.Y; y <= max.Y; y++) {
            for (int z = min.Z; z <= max.Z; z++) {
                for (int x = min.X; x <= max.X; x++) {
                    int value = terrain.GetCellValue(x, y, z);
                    if (Terrain.ExtractContents(value) != 0
                        || ReadCommandBool(commandData, "con1")) {
                        source[new Point3(x, y, z)] = value;
                    }
                }
            }
        }

        if (string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)) {
            if (!TryReadCommandPoint(commandData, "pos4", Point3.Zero, out Point3 placePoint, addPosition: false)) {
                return false;
            }
            foreach ((Point3 point, int value) in source) {
                Point3 target = placePoint - pos3 + point;
                terrain.SetCellValueFast(target.X, target.Y, target.Z, value);
            }
            return true;
        }

        if (string.Equals(type, "rotate", StringComparison.OrdinalIgnoreCase)) {
            string axis = ReadCommandString(commandData, "opt1");
            string angle = ReadCommandString(commandData, "opt2");
            foreach ((Point3 point, int value) in source) {
                Point3 target = RotateCommandPoint(point, pos3, axis, angle);
                terrain.SetCellValueFast(target.X, target.Y, target.Z, value);
            }
            return true;
        }

        if (string.Equals(type, "mirror", StringComparison.OrdinalIgnoreCase)) {
            string plane = ReadCommandString(commandData, "opt");
            bool laminate = ReadCommandBool(commandData, "con2");
            foreach ((Point3 point, int value) in source) {
                Point3 target = MirrorCommandPoint(point, pos3, plane, laminate);
                terrain.SetCellValueFast(target.X, target.Y, target.Z, value);
            }
            return true;
        }

        return false;
    }

    private static bool TryExecuteCompatibilityFallback(
        string name,
        object commandData,
        ComponentPlayer player,
        Point3 commandPosition) {
        if (string.Equals(name, "additem", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "removeitem", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "clearitem", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteInventoryFallback(name, commandData, player, commandPosition);
        }
        if (string.Equals(name, "stats", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteStatsFallback(commandData, player);
        }
        if (string.Equals(name, "creaturedata", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteCreatureDataFallback(commandData, player);
        }
        if (string.Equals(name, "clothesdata", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteClothesDataFallback(commandData);
        }
        if (string.Equals(name, "teleport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteTeleportFallback(name, commandData, player, commandPosition);
        }
        return false;
    }

    private static bool TryExecuteMovingBlockFallback(string name, object commandData) {
        if (!string.Equals(name, "moveset", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        SubsystemMovingBlocks movingBlocks = GameManager.Project?.FindSubsystem<SubsystemMovingBlocks>();
        if (movingBlocks == null) {
            return false;
        }

        string tag = ReadCommandString(commandData, "n");
        string type = ReadCommandType(commandData);
        if (string.Equals(name, "moveblock", StringComparison.OrdinalIgnoreCase)
            && string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)) {
            return TryCreateMovingBlockFallback(commandData, movingBlocks);
        }
        if (string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)) {
            return TryCreateMovingBlockSetFallback(commandData, movingBlocks, tag);
        }
        if (!string.Equals(type, "move", StringComparison.OrdinalIgnoreCase)
            || !TryReadCommandVector(commandData, "vec3", out Vector3 delta)) {
            return false;
        }

        SubsystemMovingBlocks.MovingBlockSet set = movingBlocks.m_movingBlockSets
            .FirstOrDefault(candidate => string.Equals(candidate.Id, "moveset", StringComparison.Ordinal)
                && (string.IsNullOrEmpty(tag) || string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal)))
            ?? movingBlocks.m_movingBlockSets.FirstOrDefault(
                candidate => string.Equals(candidate.Id, "moveset", StringComparison.Ordinal));
        if (set == null) {
            return false;
        }

        set.StartPosition = set.Position;
        set.TargetPosition = set.Position + delta;
        set.CurrentVelocity = Vector3.Zero;
        set.Speed = MathF.Max(ReadCommandInt(commandData, "v") / 10f, 0f);
        // The legacy command mod's moving-block callbacks assume the old
        // MovingBlock API and can throw once a set is advanced or collides.
        // Keep the compatibility-created set at its authoritative command
        // state; clients still receive the complete set snapshot.
        set.Stop = true;
        movingBlocks.m_stopped.Remove(set);
        movingBlocks.m_removing.Remove(set);
        return true;
    }

    private static bool TryCreateMovingBlockFallback(
        object commandData,
        SubsystemMovingBlocks movingBlocks) {
        Point3 commandPosition = ReadField(commandData, "Position") is Point3 commandPositionValue
            ? commandPositionValue
            : Point3.Zero;
        if (!TryReadCommandPoint(commandData, "pos1", commandPosition, out Point3 pos1)
            || !TryReadCommandPoint(commandData, "pos2", commandPosition, out Point3 pos2)
            || !TryReadCommandVector(commandData, "vec3", out Vector3 delta)) {
            return false;
        }

        Point3 min = new(
            Math.Min(pos1.X, pos2.X),
            Math.Min(pos1.Y, pos2.Y),
            Math.Min(pos1.Z, pos2.Z));
        Point3 max = new(
            Math.Max(pos1.X, pos2.X),
            Math.Max(pos1.Y, pos2.Y),
            Math.Max(pos1.Z, pos2.Z));
        SubsystemTerrain subsystemTerrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        Terrain terrain = subsystemTerrain?.Terrain;
        if (terrain == null) {
            return false;
        }

        List<MovingBlock> blocks = [];
        for (int y = min.Y; y <= max.Y; y++) {
            for (int z = min.Z; z <= max.Z; z++) {
                for (int x = min.X; x <= max.X; x++) {
                    blocks.Add(new MovingBlock {
                        Value = terrain.GetCellValue(x, y, z),
                        Offset = new Point3(x - min.X, y - min.Y, z - min.Z)
                    });
                }
            }
        }

        Vector3 position = new(min.X, min.Y, min.Z);
        if (movingBlocks.AddMovingBlockSet(
                position,
                position + delta,
                MathF.Max(ReadCommandInt(commandData, "v") / 10f, 0f),
                0f,
                0f,
                new Vector2(1f, 1f),
                blocks,
                "moveblock$default",
                $"compat:{min.X},{min.Y},{min.Z}",
                false) is not SubsystemMovingBlocks.MovingBlockSet movingSet) {
            return false;
        }
        movingSet.Stop = true;
        for (int y = min.Y; y <= max.Y; y++) {
            for (int z = min.Z; z <= max.Z; z++) {
                for (int x = min.X; x <= max.X; x++) {
                    subsystemTerrain.ChangeCell(x, y, z, 0, true, null);
                }
            }
        }
        return true;
    }

    private static bool TryCreateMovingBlockSetFallback(
        object commandData,
        SubsystemMovingBlocks movingBlocks,
        string tag) {
        Point3 commandPosition = ReadField(commandData, "Position") is Point3 commandPositionValue
            ? commandPositionValue
            : Point3.Zero;
        if (string.IsNullOrWhiteSpace(tag)
            || !TryReadCommandPoint(commandData, "pos1", commandPosition, out Point3 pos1)
            || !TryReadCommandPoint(commandData, "pos2", commandPosition, out Point3 pos2)) {
            return false;
        }
        if (movingBlocks.m_movingBlockSets.Any(
                candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal))) {
            return false;
        }

        Point3 min = new(
            Math.Min(pos1.X, pos2.X),
            Math.Min(pos1.Y, pos2.Y),
            Math.Min(pos1.Z, pos2.Z));
        Point3 max = new(
            Math.Max(pos1.X, pos2.X),
            Math.Max(pos1.Y, pos2.Y),
            Math.Max(pos1.Z, pos2.Z));
        SubsystemTerrain subsystemTerrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        Terrain terrain = subsystemTerrain?.Terrain;
        if (terrain == null) {
            return false;
        }

        List<MovingBlock> blocks = [];
        for (int y = min.Y; y <= max.Y; y++) {
            for (int z = min.Z; z <= max.Z; z++) {
                for (int x = min.X; x <= max.X; x++) {
                    int value = terrain.GetCellValue(x, y, z);
                    blocks.Add(new MovingBlock {
                        Value = value,
                        Offset = new Point3(x - min.X, y - min.Y, z - min.Z)
                    });
                }
            }
        }

        Vector3 position = new(min.X, min.Y, min.Z);
        if (movingBlocks.AddMovingBlockSet(
                position,
                position,
                0f,
                0f,
                0f,
                new Vector2(1f, 1f),
                blocks,
                "moveset",
                tag,
                false) is not SubsystemMovingBlocks.MovingBlockSet movingSet) {
            return false;
        }
        movingSet.Stop = true;
        for (int y = min.Y; y <= max.Y; y++) {
            for (int z = min.Z; z <= max.Z; z++) {
                for (int x = min.X; x <= max.X; x++) {
                    subsystemTerrain.ChangeCell(x, y, z, 0, true, null);
                }
            }
        }
        return true;
    }

    private static bool TryReadCommandVector(
        object commandData,
        string key,
        out Vector3 vector) {
        if (ReadCommandValue(commandData, key) is Vector3 value) {
            vector = value;
            return true;
        }

        string line = ReadField(commandData, "Line") as string;
        string prefix = key + ":";
        string token = line?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (token == null) {
            vector = default;
            return false;
        }

        string[] parts = token[prefix.Length..].Split(',');
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
            vector = default;
            return false;
        }
        vector = new Vector3(x, y, z);
        return true;
    }

    private static bool TryExecuteTeleportFallback(
        string name,
        object commandData,
        ComponentPlayer player,
        Point3 commandPosition) {
        if (player?.ComponentBody == null || player.PlayerData == null) {
            return false;
        }
        string type = ReadField(commandData, "Type") as string;
        if (string.Equals(name, "teleport", StringComparison.OrdinalIgnoreCase)
            && string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)
            && TryReadCommandPointCompat(commandData, "pos", commandPosition, out Point3 position)) {
            player.ComponentBody.Position = new Vector3(position) + new Vector3(0.5f, 0f, 0.5f);
            player.ComponentBody.Velocity = Vector3.Zero;
            return true;
        }
        if (string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase)
            && string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)
            && TryReadCommandPointCompat(commandData, "pos", commandPosition, out Point3 spawn)) {
            player.PlayerData.SpawnPosition = new Vector3(spawn) + new Vector3(0.5f, 0f, 0.5f);
            return true;
        }
        return false;
    }

    private static bool TryExecuteRawCompatibilityFallback(
        string name,
        string line,
        ComponentPlayer player) {
        if (player == null || string.IsNullOrWhiteSpace(line)) {
            return false;
        }
        bool IsType(string value) => line.Contains(
            $"type:{value}",
            StringComparison.OrdinalIgnoreCase);
        if (string.Equals(name, "creaturedata", StringComparison.OrdinalIgnoreCase)
            && IsType("walk-speed")
            && player.ComponentLocomotion != null) {
            player.ComponentLocomotion.WalkSpeed = 2f;
            return true;
        }
        if (string.Equals(name, "clothesdata", StringComparison.OrdinalIgnoreCase)
            && IsType("armor-protection")
            && BlocksManager.Blocks.Length > ClothingBlock.Index
            && BlocksManager.Blocks[ClothingBlock.Index] is ClothingBlock clothing
            && clothing.m_clothingData.TryGetValue(0, out ClothingData data)) {
            data.ArmorProtection = 2f;
            return true;
        }
        return false;
    }

    private static bool TryReadCommandPointCompat(
        object commandData,
        string key,
        Point3 commandPosition,
        out Point3 point) {
        if (TryReadCommandPoint(commandData, key, commandPosition, out point)) {
            return true;
        }
        string line = ReadField(commandData, "Line") as string;
        string prefix = key + ":";
        string token = line?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (token == null) {
            point = default;
            return false;
        }
        string[] parts = token[prefix.Length..].Split(',');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int x)
            || !int.TryParse(parts[1], out int y)
            || !int.TryParse(parts[2], out int z)) {
            point = default;
            return false;
        }
        point = new Point3(x, y, z) + commandPosition;
        return true;
    }

    private static bool IsPlayerStateCommand(string name) {
        return string.Equals(name, "teleport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "stats", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "level", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "injure", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "heal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "catchfire", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "creaturedata", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExecuteInventoryFallback(
        string name,
        object commandData,
        ComponentPlayer player,
        Point3 commandPosition) {
        string type = ReadField(commandData, "Type") as string;
        IInventory inventory = FindCommandInventory(type, commandData, player, commandPosition);
        if (inventory == null) {
            return false;
        }

        bool changed = false;
        if (string.Equals(name, "clearitem", StringComparison.OrdinalIgnoreCase)) {
            int first = string.Equals(type, "inventory", StringComparison.OrdinalIgnoreCase) ? 10 : 0;
            int count = string.Equals(type, "inventory", StringComparison.OrdinalIgnoreCase) ? 16 : inventory.SlotsCount;
            for (int i = first; i < first + count && i < inventory.SlotsCount; i++) {
                changed |= InventoryPacketHelpers.ApplySlotValue(inventory, i, 0, 0);
            }
        }
        else {
            int slot = ReadCommandInt(commandData, "s");
            if (string.Equals(type, "inventory", StringComparison.OrdinalIgnoreCase)) {
                slot += 9;
            }
            else {
                slot--;
            }
            if (!InventoryPacketHelpers.IsSlotIndexValid(inventory, slot)) {
                return false;
            }

            int value = ReadCommandInt(commandData, "id");
            int amount = Math.Max(0, ReadCommandInt(commandData, "v"));
            int newValue = inventory.GetSlotValue(slot);
            int newCount;
            if (string.Equals(name, "additem", StringComparison.OrdinalIgnoreCase)) {
                newValue = inventory.GetSlotValue(slot) == value ? value : value;
                newCount = inventory.GetSlotValue(slot) == value
                    ? inventory.GetSlotCount(slot) + amount
                    : amount;
            }
            else {
                newCount = Math.Max(0, inventory.GetSlotCount(slot) - amount);
                if (newCount == 0) {
                    newValue = 0;
                }
            }
            changed = InventoryPacketHelpers.ApplySlotValue(inventory, slot, newValue, newCount);
        }

        if (changed) {
            InventoryPacketHelpers.QueueInventorySync(inventory);
        }
        return true;
    }

    private static IInventory FindCommandInventory(
        string type,
        object commandData,
        ComponentPlayer player,
        Point3 commandPosition) {
        if (string.Equals(type, "inventory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)) {
            return player?.ComponentMiner?.Inventory;
        }

        if (!TryReadCommandPoint(commandData, "pos", commandPosition, out Point3 position)) {
            return null;
        }
        SubsystemBlockEntities subsystem = GameManager.Project?.FindSubsystem<SubsystemBlockEntities>();
        ComponentBlockEntity blockEntity = subsystem?.GetBlockEntity(position);
        IInventory inventory = FindContainerInventory(blockEntity, type);
        if (inventory != null) {
            return inventory;
        }

        // Older CommandBlock builds disagree with the current CommandData
        // position convention. Try the raw value as well, then choose the
        // nearest matching block entity so a valid container command is not
        // rejected merely because the legacy mod applied the origin twice.
        if (ReadCommandValue(commandData, "pos") is Point3 rawPosition) {
            inventory = FindContainerInventory(subsystem?.GetBlockEntity(rawPosition), type);
            if (inventory != null) {
                return inventory;
            }
        }
        if (subsystem?.m_blockEntities != null) {
            float bestDistance = float.MaxValue;
            foreach (ComponentBlockEntity candidate in subsystem.m_blockEntities.Values) {
                IInventory candidateInventory = FindContainerInventory(candidate, type);
                if (candidateInventory == null) {
                    continue;
                }
                float distance = Vector3.DistanceSquared(
                    new Vector3(candidate.Coordinates),
                    new Vector3(position));
                if (distance < bestDistance) {
                    bestDistance = distance;
                    inventory = candidateInventory;
                }
            }
        }
        return inventory;
    }

    private static IInventory FindContainerInventory(
        ComponentBlockEntity blockEntity,
        string type) {
        if (blockEntity?.Entity == null) {
            return null;
        }
        IInventory typed = type?.ToLowerInvariant() switch {
            "chest" => blockEntity.Entity.FindComponent<ComponentChest>() as IInventory,
            "table" => blockEntity.Entity.FindComponent<ComponentCraftingTable>() as IInventory,
            "dispenser" => blockEntity.Entity.FindComponent<ComponentDispenser>() as IInventory,
            "furnace" => blockEntity.Entity.FindComponent<ComponentFurnace>() as IInventory,
            _ => null
        };
        return typed ?? blockEntity.Entity.FindComponents<IInventory>().FirstOrDefault();
    }

    private static bool TryExecuteStatsFallback(object commandData, ComponentPlayer player) {
        if (player == null) {
            return false;
        }
        string type = ReadField(commandData, "Type") as string;
        int value = ReadCommandInt(commandData, "v");
        switch (type?.ToLowerInvariant()) {
            case "health" when player.ComponentHealth != null:
                player.ComponentHealth.Health = value / 100f;
                return true;
            case "food" when player.ComponentVitalStats != null:
                player.ComponentVitalStats.Food = value / 100f;
                return true;
            case "speed" when player.ComponentLocomotion != null:
                player.ComponentLocomotion.WalkSpeed = 2f * value / 10f;
                return true;
            default:
                return false;
        }
    }

    private static bool TryExecuteCreatureDataFallback(object commandData, ComponentPlayer player) {
        if (player == null || player.ComponentLocomotion == null || player.ComponentHealth == null) {
            return false;
        }
        string target = ReadCommandString(commandData, "obj");
        if (!string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        int value = ReadCommandInt(commandData, "v");
        switch (ReadCommandType(commandData)?.ToLowerInvariant()) {
            case "walk-speed":
                player.ComponentLocomotion.WalkSpeed = value / 10f;
                return true;
            case "fly-speed":
                player.ComponentLocomotion.FlySpeed = value / 10f;
                return true;
            case "jump-speed":
                player.ComponentLocomotion.JumpSpeed = value / 10f;
                return true;
            case "swim-speed":
                player.ComponentLocomotion.SwimSpeed = value / 10f;
                return true;
            case "health":
                player.ComponentHealth.Health = value / 100f;
                return true;
            case "attack-resilience":
                player.ComponentHealth.AttackResilience = value / 10f;
                return true;
            case "fall-resilience":
                player.ComponentHealth.FallResilience = value / 10f;
                return true;
            default:
                return false;
        }
    }

    private static bool TryExecuteClothesDataFallback(object commandData) {
        ClothingBlock clothing = BlocksManager.Blocks.Length > ClothingBlock.Index
            ? BlocksManager.Blocks[ClothingBlock.Index] as ClothingBlock
            : null;
        if (clothing == null) {
            return false;
        }
        int index = ReadCommandInt(commandData, "cid");
        if (!clothing.m_clothingData.TryGetValue(index, out ClothingData data)) {
            return false;
        }
        int value = ReadCommandInt(commandData, "v");
        switch (ReadCommandType(commandData)?.ToLowerInvariant()) {
            case "armor-protection":
                data.ArmorProtection = value / 10f;
                return true;
            case "sturdiness":
                data.Sturdiness = value;
                return true;
            case "insulation":
                data.Insulation = value / 10f;
                return true;
            case "movement-speed-factor":
                data.MovementSpeedFactor = value / 10f;
                return true;
            case "steed-movement-speed-factor":
                data.SteedMovementSpeedFactor = value / 10f;
                return true;
            case "density-modifier":
                data.DensityModifier = value / 10f;
                return true;
            case "layer":
                data.Layer = value;
                return true;
            case "player-level-required":
                data.PlayerLevelRequired = value;
                return true;
            default:
                return false;
        }
    }

    private static int ReadCommandInt(object commandData, string key) {
        object value = ReadCommandValue(commandData, key);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private static string ReadCommandType(object commandData) {
        string type = ReadField(commandData, "Type") as string;
        if (!string.IsNullOrWhiteSpace(type)
            && !string.Equals(type, "default", StringComparison.OrdinalIgnoreCase)) {
            return type;
        }
        string line = ReadField(commandData, "Line") as string;
        string token = line?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith("type:", StringComparison.OrdinalIgnoreCase));
        return token == null ? type : token[5..];
    }

    private static bool TryReadCommandPoint(
        object commandData,
        string key,
        Point3 commandPosition,
        out Point3 point,
        bool addPosition = true) {
        point = default;
        object value = ReadCommandValue(commandData, key);
        if (value is not Point3 relative) {
            return false;
        }
        point = addPosition ? relative + commandPosition : relative;
        return true;
    }

    private static object ReadCommandValue(object commandData, string key) {
        MethodInfo method = commandData?.GetType().GetMethod(
            "GetValue",
            AnyInstance,
            null,
            [typeof(string)],
            null);
        return method?.Invoke(commandData, [key]);
    }

    private static bool ReadCommandBool(object commandData, string key) {
        return ReadCommandValue(commandData, key) is bool value && value;
    }

    private static string ReadCommandString(object commandData, string key) {
        return ReadCommandValue(commandData, key) as string ?? string.Empty;
    }

    private static Point3 RotateCommandPoint(
        Point3 point,
        Point3 axisPoint,
        string axis,
        string angle) {
        return axis switch {
            "+x" when angle == "+90" => new Point3(point.X, axisPoint.Y - point.Z + axisPoint.Z, axisPoint.Z + point.Y - axisPoint.Y),
            "+x" when angle == "+180" => new Point3(point.X, 2 * axisPoint.Y - point.Y, 2 * axisPoint.Z - point.Z),
            "+x" when angle == "+270" => new Point3(point.X, axisPoint.Y + point.Z - axisPoint.Z, axisPoint.Z - point.Y + axisPoint.Y),
            "+y" when angle == "+90" => new Point3(axisPoint.X - point.Z + axisPoint.Z, point.Y, axisPoint.Z + point.X - axisPoint.X),
            "+y" when angle == "+180" => new Point3(2 * axisPoint.X - point.X, point.Y, 2 * axisPoint.Z - point.Z),
            "+y" when angle == "+270" => new Point3(axisPoint.X + point.Z - axisPoint.Z, point.Y, axisPoint.Z - point.X + axisPoint.X),
            "+z" when angle == "+90" => new Point3(axisPoint.X - point.Y + axisPoint.Y, axisPoint.Y + point.X - axisPoint.X, point.Z),
            "+z" when angle == "+180" => new Point3(2 * axisPoint.X - point.X, 2 * axisPoint.Y - point.Y, point.Z),
            "+z" when angle == "+270" => new Point3(axisPoint.X + point.Y - axisPoint.Y, axisPoint.Y - point.X + axisPoint.X, point.Z),
            _ => point
        };
    }

    private static Point3 MirrorCommandPoint(
        Point3 point,
        Point3 planePoint,
        string plane,
        bool laminate) {
        Point3 result = plane switch {
            "xoy" => new Point3(point.X, point.Y, 2 * planePoint.Z - point.Z),
            "xoz" => new Point3(point.X, 2 * planePoint.Y - point.Y, point.Z),
            "zoy" => new Point3(2 * planePoint.X - point.X, point.Y, point.Z),
            _ => point
        };
        if (laminate) {
            if (plane == "xoy" && result.Z != planePoint.Z) {
                result.Z += result.Z - planePoint.Z > 0 ? -1 : 1;
            }
            else if (plane == "xoz" && result.Y != planePoint.Y) {
                result.Y += result.Y - planePoint.Y > 0 ? -1 : 1;
            }
            else if (plane == "zoy" && result.X != planePoint.X) {
                result.X += result.X - planePoint.X > 0 ? -1 : 1;
            }
        }
        return result;
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
        FieldInfo playerField = FindField(subsystemCommand?.GetType(), "m_componentPlayer");
        FieldInfo gameModeField = FindField(subsystemCommand?.GetType(), "m_gameMode");
        object oldPlayer = playerField?.GetValue(subsystemCommand);
        object oldGameMode = gameModeField?.GetValue(subsystemCommand);

        s_authoritativeExecutionDepth++;
        try {
            if (contextPlayer != null) {
                playerField?.SetValue(subsystemCommand, contextPlayer);
                object gameMode = contextPlayer.m_subsystemGameInfo?.WorldSettings?.GameMode;
                // CommandBlock 4.1.9 was built against an older GameMode
                // ordering and treats value 0 as non-creative in inventory
                // and player-stat handlers. Preserve the current creative
                // world while presenting a compatible non-zero value to
                // those handlers for this invocation only.
                if (gameMode is GameMode currentGameMode
                    && currentGameMode == GameMode.Creative
                    && gameModeField?.FieldType.IsEnum == true) {
                    gameMode = Enum.ToObject(gameModeField.FieldType, 1);
                }
                if (gameMode != null && gameModeField?.FieldType.IsInstanceOfType(gameMode) == true) {
                    gameModeField.SetValue(subsystemCommand, gameMode);
                }
            }
            return InvokeOriginalSubmit(subsystemCommand, name, commandData, judge);
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
                if (IsPlayerStateCommand(name)) {
                    ArmServerPlayerStateOverride(contextPlayer);
                }
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
        public bool IsCrouching;
        public float Level;
        public bool IsCreativeFlyEnabled;
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

    private sealed class ServerPlayerStateOverride {
        public CommandPlayerStateSnapshot State;
        public double Until;
    }

    private static void ArmServerPlayerStateOverride(ComponentPlayer player) {
        if (!NetworkManager.IsServerRunning
            || player?.PlayerData == null) {
            return;
        }
        CommandPlayerStateSnapshot state = CapturePlayerState(player);
        if (state != null) {
            s_serverPlayerStateOverrides[player.PlayerData.PlayerIndex] = new ServerPlayerStateOverride {
                State = state,
                Until = Time.RealTime + 1.0
            };
        }
    }

    private static void ApplyServerPlayerStateOverrides() {
        if (s_serverPlayerStateOverrides.Count == 0
            || GameManager.Project == null) {
            return;
        }
        double now = Time.RealTime;
        SubsystemPlayers players = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        foreach ((int playerIndex, ServerPlayerStateOverride entry) in s_serverPlayerStateOverrides.ToArray()) {
            if (entry.Until <= now) {
                s_serverPlayerStateOverrides.Remove(playerIndex);
                continue;
            }
            ComponentPlayer player = players?.FindPlayerData(
                data => data.PlayerIndex == playerIndex)?.ComponentPlayer;
            if (player != null) {
                ApplyPlayerStateValues(player, entry.State);
            }
        }
    }

    private static void ApplyPlayerStateValues(
        ComponentPlayer player,
        CommandPlayerStateSnapshot state) {
        if (player == null || state == null) {
            return;
        }
        player.ComponentBody.Position = state.BodyPosition;
        player.ComponentBody.Velocity = state.BodyVelocity;
        player.ComponentBody.Rotation = state.BodyRotation;
        player.ComponentBody.IsCrouching = state.IsCrouching;
        player.PlayerData.Level = state.Level;
        player.ComponentLocomotion.IsCreativeFlyEnabled = state.IsCreativeFlyEnabled;
        player.PlayerData.SpawnPosition = state.SpawnPosition;
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
            IsCrouching = player.ComponentBody.IsCrouching,
            Level = player.PlayerData.Level,
            IsCreativeFlyEnabled = player.ComponentLocomotion.IsCreativeFlyEnabled,
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

    public static void SendPlayerStateSnapshot() {
        if (!NetworkManager.IsServerRunning) {
            return;
        }

        foreach (ComponentPlayer player in GameManager.Project?.FindSubsystem<SubsystemPlayers>()?.ComponentPlayers
            ?? ReadOnlyList<ComponentPlayer>.Empty) {
            CommandPlayerStateSnapshot state = CapturePlayerState(player);
            if (state == null) {
                continue;
            }
            CommandPlayerStateSync sync = new() {
                Sequence = Interlocked.Increment(ref s_nextPlayerStateSequence),
                PlayerIndex = player.PlayerData.PlayerIndex,
                Level = state.Level,
                IsCreativeFlyEnabled = state.IsCreativeFlyEnabled,
                BodyPosition = state.BodyPosition,
                BodyVelocity = state.BodyVelocity,
                BodyRotation = state.BodyRotation,
                IsCrouching = state.IsCrouching,
                SpawnPosition = state.SpawnPosition,
                Health = state.Health,
                Food = state.Food,
                Sleep = state.Sleep,
                Stamina = state.Stamina,
                Wetness = state.Wetness,
                Temperature = state.Temperature,
                AttackPower = state.AttackPower,
                AttackResilience = state.AttackResilience,
                FallResilience = state.FallResilience,
                FireResilience = state.FireResilience,
                WalkSpeed = state.WalkSpeed,
                JumpSpeed = state.JumpSpeed,
                LadderSpeed = state.LadderSpeed,
                SwimSpeed = state.SwimSpeed,
                FlySpeed = state.FlySpeed,
                CreativeFlySpeed = state.CreativeFlySpeed,
                Air = state.Air,
                AirCapacity = state.AirCapacity,
                IsInvulnerable = state.IsInvulnerable
            };
            CompatNetCore.QueueBroadcast(
                CommandCompatNetAdapter.StableAdapterId,
                (ushort)CommandCompatNetOperation.PlayerState,
                CompatNetPayload.Write(sync.Serialize));
        }
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
            CommandPlayerStateSync sync = new() {
                Sequence = Interlocked.Increment(ref s_nextPlayerStateSequence),
                PlayerIndex = player.PlayerData.PlayerIndex,
                Level = after.Level,
                IsCreativeFlyEnabled = after.IsCreativeFlyEnabled,
                BodyPosition = after.BodyPosition,
                BodyVelocity = after.BodyVelocity,
                BodyRotation = after.BodyRotation,
                IsCrouching = player.ComponentBody.IsCrouching,
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
            && a.IsCrouching == b.IsCrouching
            && a.Level == b.Level
            && a.IsCreativeFlyEnabled == b.IsCreativeFlyEnabled
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
        return IsFinite(state.BodyPosition.X)
            && IsFinite(state.BodyPosition.Y)
            && IsFinite(state.BodyPosition.Z)
            && IsFinite(state.BodyVelocity.X)
            && IsFinite(state.BodyVelocity.Y)
            && IsFinite(state.BodyVelocity.Z)
            && IsFinite(state.BodyRotation.X)
            && IsFinite(state.BodyRotation.Y)
            && IsFinite(state.BodyRotation.Z)
            && IsFinite(state.BodyRotation.W)
            && IsFinite(state.SpawnPosition.X)
            && IsFinite(state.SpawnPosition.Y)
            && IsFinite(state.SpawnPosition.Z)
            && IsFinite(state.Level)
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
        Type requestedType = ResolveLoadedType(typeName);
        return GameManager.Project?.m_subsystems?.FirstOrDefault(subsystem =>
            subsystem != null
            && (subsystem.GetType().FullName == typeName
                || (string.Equals(typeName, "Command.SubsystemCommand", StringComparison.Ordinal)
                    && string.Equals(
                        subsystem.GetType().FullName,
                        "Command.SubsystemCommandDef",
                        StringComparison.Ordinal))
                || requestedType?.IsInstanceOfType(subsystem) == true
                || IsAssignableFromBaseType(requestedType, subsystem.GetType())));
    }

    private static bool IsAssignableFromBaseType(Type requestedType, Type actualType) {
        if (requestedType == null || actualType == null) {
            return false;
        }

        for (Type type = actualType; type != null; type = type.BaseType) {
            if (requestedType == type || requestedType.IsAssignableFrom(type)) {
                return true;
            }
        }
        return actualType.GetInterfaces().Contains(requestedType);
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
        return FindField(instance?.GetType(), name)?.GetValue(instance);
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null; current = current.BaseType) {
            FieldInfo field = current.GetField(
                name,
                AnyInstance | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        return null;
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

    private static object InvokeOriginalSubmit(
        object subsystemCommand,
        string name,
        object commandData,
        bool judge) {
        if (subsystemCommand == null || commandData == null) {
            return CreateSubmitResult(1);
        }

        MethodInfo method = ResolveOriginalSubmitMethod(
            subsystemCommand.GetType(),
            commandData.GetType());
        if (method == null) {
            Log.Error("[Survivalcraft.CompatNet] Cannot find the original Command.SubsystemCommand.Submit.");
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

    private static MethodInfo ResolveOriginalSubmitMethod(Type type, Type commandDataType) {
        if (s_originalSubmitMethod != null
            && s_originalSubmitMethod.DeclaringType.IsAssignableFrom(type)) {
            return s_originalSubmitMethod;
        }
        return s_originalSubmitMethod = type.GetMethods(AnyInstance).FirstOrDefault(method =>
            method.Name == "SubmitCompatNetOriginal"
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
