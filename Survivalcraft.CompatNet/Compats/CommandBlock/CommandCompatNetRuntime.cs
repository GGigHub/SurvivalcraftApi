using System.Reflection;
using Engine;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Reflection bridge used by the in-memory CommandBlock patch. Keeping the
/// bridge independent from CommandBlock.dll lets CompatNet load without the
/// optional mod installed.
/// </summary>
public static class CommandCompatNetRuntime {
    public const int MaximumCommandLineChars = 8192;

    private const string CommandDataTypeName = "Command.CommandData";
    private const string CommandSubsystemTypeName = "Command.SubsystemCommand";
    private const string CommandBlockBehaviorTypeName = "Command.SubsystemCommandBlockBehavior";
    private const string CommandBlockTypeName = "Command.CommandBlock";
    private const string SubmitResultTypeName = "Command.SubmitResult";

    private static readonly BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags AnyStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type s_commandDataType;
    private static Type s_commandSubsystemType;
    private static Type s_submitResultType;
    private static FieldInfo s_commandPlayerField;
    private static FieldInfo s_commandElectricVoltageField;
    private static MethodInfo s_submitMethod;
    private static readonly Dictionary<Point3, float> s_lastElectricVoltages = [];

    public static void Reset() {
        s_commandDataType = null;
        s_commandSubsystemType = null;
        s_submitResultType = null;
        s_commandPlayerField = null;
        s_commandElectricVoltageField = null;
        s_submitMethod = null;
        s_lastElectricVoltages.Clear();
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
    /// Replaces direct Submit calls made by command-rod and UI code.
    /// </summary>
    public static object SubmitCommandBridge(
        object subsystemCommand,
        string name,
        object commandData,
        bool judge) {
        if (IsClientOnly()) {
            ComponentPlayer player = ResolveCommandPlayer(subsystemCommand);
            if (player != null && TryReadCommandData(commandData, out Point3 position, out string line, out _)) {
                CommandExecuteRequest request = new() {
                    Position = position,
                    Line = line,
                    Judge = judge,
                    Source = CommandExecutionSource.Manual
                };
                bool queued = CompatNetCore.QueueRequest(
                    player,
                    CommandCompatNetAdapter.StableAdapterId,
                    (ushort)CommandCompatNetOperation.Execute,
                    CompatNetPayload.Write(request.Serialize));
                return CreateSubmitResult(queued ? 0 : 1);
            }
            return CreateSubmitResult(1);
        }

        return InvokeSubmit(subsystemCommand, name, commandData, judge);
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

        object result = InvokeSubmit(subsystemCommand, name, commandData, judge);
        if (TryReadCommandData(commandData, out Point3 position, out _, out bool hasPosition)) {
            CommandElectricResult electricResult = new() {
                Position = position,
                Voltage = GetSubmitResultValue(result) == 0 ? 1f : 0f
            };
            if (hasPosition
                && IsElectricOutputMode(commandData)
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
            CellValue = newCellValue,
            Line = canonicalLine
        };
        return true;
    }

    public static void ExecuteCommandRequest(
        ComponentPlayer player,
        Point3 position,
        string line,
        bool judge,
        CommandExecutionSource source) {
        if (player == null
            || !IsValidLineLength(line)
            || (source == CommandExecutionSource.Manual && !HasCommandRodEquipped(player))) {
            return;
        }

        if (source == CommandExecutionSource.Manual) {
            judge = false;
        }

        object subsystemCommand = FindSubsystem(CommandSubsystemTypeName);
        Type commandDataType = ResolveCommandDataType();
        if (subsystemCommand == null || commandDataType == null) {
            return;
        }

        try {
            object commandData = Activator.CreateInstance(
                commandDataType,
                position,
                line ?? string.Empty);
            MethodInfo trySetValue = commandDataType.GetMethod("TrySetValue", AnyInstance, null, Type.EmptyTypes, null);
            trySetValue?.Invoke(commandData, null);
            if (!IsCommandDataExecutable(commandData)) {
                return;
            }

            FieldInfo playerField = ResolveCommandPlayerField(subsystemCommand.GetType());
            object previousPlayer = playerField?.GetValue(subsystemCommand);
            playerField?.SetValue(subsystemCommand, player);
            try {
                string name = ReadField(commandData, "Name") as string;
                if (!string.IsNullOrEmpty(name)) {
                    InvokeSubmit(subsystemCommand, name, commandData, judge);
                }
            }
            finally {
                playerField?.SetValue(subsystemCommand, previousPlayer);
            }
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to execute command request: {e}");
        }
    }

    public static void ApplyCommandBlockSync(CommandBlockSync sync) {
        if (sync == null) {
            return;
        }

        try {
            object behavior = FindSubsystem(CommandBlockBehaviorTypeName);
            SetCommandData(behavior, sync.Position, sync.Line ?? string.Empty);
            SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
            if (terrain != null && terrain.Terrain.IsCellValid(sync.Position.X, sync.Position.Y, sync.Position.Z)) {
                terrain.ChangeCell(
                    sync.Position.X,
                    sync.Position.Y,
                    sync.Position.Z,
                    sync.CellValue,
                    true,
                    null,
                    true);
            }
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

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }

    private static bool IsValidLineLength(string line) {
        return (line?.Length ?? 0) <= MaximumCommandLineChars;
    }

    private static bool IsWithinEditRange(ComponentPlayer player, Point3 position) {
        if (player.ComponentBody == null) {
            return false;
        }

        Vector3 center = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
        return Vector3.DistanceSquared(player.ComponentBody.Position, center) <= 64f;
    }

    private static bool HasCommandRodEquipped(ComponentPlayer player) {
        IInventory inventory = player.ComponentMiner?.Inventory;
        if (inventory == null) {
            return false;
        }

        int value = inventory.GetSlotValue(inventory.ActiveSlotIndex);
        return Terrain.ExtractContents(value) == ResolveBlockIndex("Command.CmdRodBlock", 334);
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

    private static Type ResolveCommandDataType() {
        return s_commandDataType ??= ResolveLoadedType(CommandDataTypeName);
    }

    private static Type ResolveCommandSubsystemType() {
        return s_commandSubsystemType ??= ResolveLoadedType(CommandSubsystemTypeName);
    }

    private static Type ResolveSubmitResultType() {
        return s_submitResultType ??= ResolveLoadedType(SubmitResultTypeName);
    }

    private static FieldInfo ResolveCommandPlayerField(Type type) {
        if (s_commandPlayerField != null && s_commandPlayerField.DeclaringType.IsAssignableFrom(type)) {
            return s_commandPlayerField;
        }
        return s_commandPlayerField = type.GetField("m_componentPlayer", AnyInstance)
            ?? ResolveCommandSubsystemType()?.GetField("m_componentPlayer", AnyInstance);
    }

    private static ComponentPlayer ResolveCommandPlayer(object subsystemCommand) {
        if (subsystemCommand == null) {
            return null;
        }
        return ResolveCommandPlayerField(subsystemCommand.GetType())?.GetValue(subsystemCommand) as ComponentPlayer;
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

    private static bool IsCommandDataExecutable(object commandData) {
        bool valid = ReadField(commandData, "Valid") is bool isValid && isValid;
        bool outRange = ReadField(commandData, "OutRange") is bool isOutRange && isOutRange;
        return valid && !outRange && !string.IsNullOrEmpty(ReadField(commandData, "Name") as string);
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
        out string line,
        out bool hasPosition) {
        position = default;
        line = string.Empty;
        hasPosition = commandData != null;
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
