using System.Text.Json;
using Engine;
using Game;

namespace Survivalcraft.AndroidTestAutomation;

public sealed class AndroidDeviceTestAutomationModLoader : ModLoader {
    public const int ProtocolVersion = 1;
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumWidgetCount = 512;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string m_rootDirectory = $"{ModsManager.DataPath}/AutoTest";
    private readonly string m_inboxDirectory;
    private readonly string m_processedDirectory;
    private readonly string m_responseDirectory;
    private int m_lastProcessedFrame = -1;

    public AndroidDeviceTestAutomationModLoader() {
        m_inboxDirectory = $"{m_rootDirectory}/inbox";
        m_processedDirectory = $"{m_rootDirectory}/processed";
        m_responseDirectory = $"{m_rootDirectory}/responses";
    }

    public override void __ModInitialize() {
        Storage.CreateDirectory(m_rootDirectory);
        Storage.CreateDirectory(m_inboxDirectory);
        Storage.CreateDirectory(m_processedDirectory);
        Storage.CreateDirectory(m_responseDirectory);
        ModsManager.RegisterHook("BeforeWidgetUpdate", this);
        WriteAtomic(
            $"{m_rootDirectory}/bridge.json",
            new {
                schemaVersion = ProtocolVersion,
                ready = true,
                transport = "adb-file-inbox",
                commands = SupportedCommands,
                maximumRequestBytes = MaximumRequestBytes,
                maximumWidgetCount = MaximumWidgetCount
            });
        Log.Information(
            $"Android device test automation bridge ready at '{m_rootDirectory}'.");
    }

    public override void BeforeWidgetUpdate(Widget widget) {
        Widget? automationRoot = GetAutomationRoot();
        if (automationRoot == null
            || widget != automationRoot
            || m_lastProcessedFrame == Time.FrameIndex) {
            return;
        }
        m_lastProcessedFrame = Time.FrameIndex;
        ProcessNextRequest();
    }

    private void ProcessNextRequest() {
        string? fileName;
        try {
            fileName = Storage.ListFileNames(m_inboxDirectory)
                .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException) {
            Storage.CreateDirectory(m_inboxDirectory);
            return;
        }
        if (string.IsNullOrEmpty(fileName)) {
            return;
        }

        string requestPath = Storage.CombinePaths(m_inboxDirectory, fileName);
        string fallbackId = Path.GetFileNameWithoutExtension(fileName);
        AndroidAutomationRequest? request = null;
        AndroidAutomationResponse response;
        try {
            long length = Storage.GetFileSize(requestPath);
            if (length <= 0 || length > MaximumRequestBytes) {
                throw new InvalidDataException(
                    $"Request size {length} is outside 1..{MaximumRequestBytes} bytes.");
            }
            request = JsonSerializer.Deserialize<AndroidAutomationRequest>(
                Storage.ReadAllText(requestPath),
                JsonOptions)
                ?? throw new InvalidDataException("Request JSON is empty.");
            ValidateRequest(request, fallbackId);
            object? data = Execute(request.Command, request.Arguments);
            response = AndroidAutomationResponse.Success(request, data);
            Log.Information(
                $"Android device test command completed: {request.Id} {request.Command}");
        }
        catch (Exception ex) {
            string id = IsSafeId(request?.Id) ? request!.Id : fallbackId;
            string command = request?.Command ?? string.Empty;
            response = AndroidAutomationResponse.Failure(id, command, ex);
            Log.Error(
                $"Android device test command failed: {id} {command}\n{ex}");
        }
        finally {
            string processedPath = Storage.CombinePaths(m_processedDirectory, fileName);
            try {
                Storage.MoveFile(requestPath, processedPath);
            }
            catch (Exception ex) {
                Log.Error($"Could not archive Android device test request '{fileName}': {ex}");
            }
        }

        string responseId = IsSafeId(response.Id) ? response.Id : fallbackId;
        WriteAtomic(
            Storage.CombinePaths(m_responseDirectory, $"{responseId}.json"),
            response);
    }

    private static void ValidateRequest(AndroidAutomationRequest request, string fileId) {
        if (request.SchemaVersion != ProtocolVersion) {
            throw new InvalidDataException(
                $"Unsupported schemaVersion {request.SchemaVersion}; expected {ProtocolVersion}.");
        }
        if (!IsSafeId(request.Id)) {
            throw new InvalidDataException(
                "Request id must contain only letters, digits, '.', '_' or '-' and be at most 96 characters.");
        }
        if (!string.Equals(request.Id, fileId, StringComparison.Ordinal)) {
            throw new InvalidDataException(
                $"Request id '{request.Id}' does not match filename id '{fileId}'.");
        }
        if (string.IsNullOrWhiteSpace(request.Command)) {
            throw new InvalidDataException("Command cannot be empty.");
        }
    }

    private object? Execute(string command, JsonElement arguments) {
        return command.Trim().ToLowerInvariant() switch {
            "system.ping" => BuildPing(),
            "state.get" => BuildState(),
            "settings.set" => SetSettings(arguments),
            "world.list" => ListWorlds(),
            "world.create" => CreateWorld(arguments),
            "world.play" => PlayWorld(arguments),
            "world.exit" => ExitWorld(arguments),
            "world.delete" => DeleteWorld(arguments),
            "player.enter" => EnterPlayer(arguments),
            "network.startrelayhost" => StartRelayHost(),
            "mods.list" => ListMods(),
            "ui.tree" => BuildWidgetTree(),
            "ui.click" => ClickWidget(arguments),
            "ui.settext" => SetWidgetText(arguments),
            _ => throw new InvalidOperationException(
                $"Unknown command '{command}'. Supported commands: {string.Join(", ", SupportedCommands)}")
        };
    }

    private static object BuildPing() => new {
        message = "pong",
        protocolVersion = ProtocolVersion,
        transport = "adb-file-inbox",
        commands = SupportedCommands,
        frameIndex = Time.FrameIndex,
        utc = DateTimeOffset.UtcNow
    };

    private static object BuildState() {
        SubsystemPlayers? players = GameManager.Project?.FindSubsystem<SubsystemPlayers>();
        PlayerData? mainPlayer = players?.MainPlayerData;
        Dialog? dialog = DialogsManager.Dialogs.Count > 0
            ? DialogsManager.Dialogs[DialogsManager.Dialogs.Count - 1]
            : null;
        return new {
            contentLoaded = LoadingScreen.m_isContentLoaded,
            screen = ScreensManager.CurrentScreen?.GetType().Name ?? string.Empty,
            dialog = dialog?.GetType().Name ?? string.Empty,
            projectLoaded = GameManager.Project != null,
            networkProject = GameManager.IsNetworkProject,
            worldName = GameManager.WorldInfo?.WorldSettings?.Name ?? string.Empty,
            worldDirectory = GameManager.WorldInfo?.DirectoryName ?? string.Empty,
            playerReady = mainPlayer?.IsReadyForPlaying == true
                && mainPlayer.ComponentPlayer != null,
            moveControlMode = SettingsManager.MoveControlMode.ToString(),
            playWithFriendsEnabled = SettingsManager.PlayWithFriendsEnabled,
            showPlayWithFriendsDialog = SettingsManager.ShowPlayWithFriendsDialog,
            serverRunning = NetworkManager.IsServerRunning,
            clientRunning = NetworkManager.IsClientRunning,
            relayStatus = NetworkManager.RelayHostCodeStatus.ToString(),
            relayGlobalCode = NetworkManager.RelayGlobalCode ?? string.Empty,
            relayRoomCode = NetworkManager.RelayRoomCode ?? string.Empty,
            relayEndPoint = NetworkManager.RelayHostEndPoint ?? string.Empty,
            relayAttemptCount = NetworkManager.RelayHostAttemptCount,
            relayLastFailure = NetworkManager.RelayHostLastFailure ?? string.Empty,
            frameIndex = Time.FrameIndex,
            realTime = Time.RealTime
        };
    }

    private static object SetSettings(JsonElement arguments) {
        if (TryGetProperty(arguments, "moveControlMode", out JsonElement moveMode)) {
            string value = moveMode.GetString()
                ?? throw new ArgumentException("moveControlMode cannot be null.");
            if (!Enum.TryParse(value, true, out MoveControlMode parsed)) {
                throw new ArgumentException(
                    $"Unknown moveControlMode '{value}'. Expected Buttons or Pad.");
            }
            SettingsManager.MoveControlMode = parsed;
        }
        if (TryGetProperty(arguments, "playWithFriendsEnabled", out JsonElement enabled)) {
            SettingsManager.PlayWithFriendsEnabled = enabled.GetBoolean();
        }
        if (TryGetProperty(arguments, "showPlayWithFriendsDialog", out JsonElement showDialog)) {
            SettingsManager.ShowPlayWithFriendsDialog = showDialog.GetBoolean();
        }
        if (GetBoolean(arguments, "save", true)) {
            SettingsManager.SaveSettings();
        }
        return BuildState();
    }

    private static object ListWorlds() {
        EnsureContentLoaded();
        WorldsManager.UpdateWorldsList();
        return new {
            worlds = WorldsManager.WorldInfos.Select(world => new {
                name = world.WorldSettings?.Name ?? string.Empty,
                directoryName = world.DirectoryName,
                playerCount = world.PlayerInfos.Count,
                size = world.Size,
                lastSaveTime = world.LastSaveTime
            }).ToArray()
        };
    }

    private static object CreateWorld(JsonElement arguments) {
        EnsureContentLoaded();
        ReturnToMainMenu(saveWorld: false);
        string name = GetString(arguments, "name", $"AndroidAutoTest-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        string seed = GetString(arguments, "seed", "android-device-autotest");
        WorldSettings settings = new() {
            Name = name,
            Seed = seed,
            GameMode = GetBoolean(arguments, "creative", false)
                ? GameMode.Creative
                : GameMode.Survival,
            TerrainGenerationMode = GetBoolean(arguments, "flatTerrain", true)
                ? TerrainGenerationMode.FlatContinent
                : TerrainGenerationMode.Continent,
            OriginalSerializationVersion = VersionsManager.SerializationVersion,
            EnvironmentBehaviorMode = EnvironmentBehaviorMode.Static,
            TimeOfDayMode = TimeOfDayMode.Day,
            AreWeatherEffectsEnabled = false,
            AreSeasonsChanging = false
        };
        WorldInfo world = WorldsManager.CreateWorld(settings);
        bool playWithFriends = GetBoolean(arguments, "playWithFriends", false);
        ScreensManager.SwitchScreen("GameLoading", world, null, playWithFriends);
        return new {
            worldName = world.WorldSettings.Name,
            directoryName = world.DirectoryName,
            playWithFriends
        };
    }

    private static object PlayWorld(JsonElement arguments) {
        EnsureContentLoaded();
        bool saveCurrent = GetBoolean(arguments, "saveCurrent", false);
        ReturnToMainMenu(saveCurrent);
        WorldsManager.UpdateWorldsList();
        string directoryName = GetString(arguments, "directoryName", string.Empty);
        string worldName = GetString(arguments, "worldName", string.Empty);
        WorldInfo? world = WorldsManager.WorldInfos.FirstOrDefault(candidate =>
            (!string.IsNullOrEmpty(directoryName)
                && string.Equals(candidate.DirectoryName, directoryName, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(worldName)
                && string.Equals(candidate.WorldSettings?.Name, worldName, StringComparison.OrdinalIgnoreCase)));
        if (world == null) {
            throw new InvalidOperationException(
                $"World not found (directoryName='{directoryName}', worldName='{worldName}').");
        }
        bool playWithFriends = GetBoolean(arguments, "playWithFriends", false);
        ScreensManager.SwitchScreen("GameLoading", world, null, playWithFriends);
        return new {
            worldName = world.WorldSettings?.Name ?? string.Empty,
            directoryName = world.DirectoryName,
            playWithFriends
        };
    }

    private static object ExitWorld(JsonElement arguments) {
        bool saveWorld = GetBoolean(arguments, "save", true);
        string directoryName = GameManager.WorldInfo?.DirectoryName ?? string.Empty;
        ReturnToMainMenu(saveWorld);
        return new { directoryName, saved = saveWorld };
    }

    private static object DeleteWorld(JsonElement arguments) {
        EnsureContentLoaded();
        string directoryName = GetRequiredString(arguments, "directoryName");
        if (GameManager.WorldInfo != null
            && string.Equals(
                GameManager.WorldInfo.DirectoryName,
                directoryName,
                StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Cannot delete the currently loaded world.");
        }
        WorldsManager.UpdateWorldsList();
        WorldInfo? world = WorldsManager.WorldInfos.FirstOrDefault(candidate =>
            string.Equals(candidate.DirectoryName, directoryName, StringComparison.OrdinalIgnoreCase));
        if (world == null) {
            return new { directoryName, deleted = false };
        }
        WorldsManager.DeleteWorld(world.DirectoryName);
        WorldsManager.UpdateWorldsList();
        return new { directoryName, deleted = true };
    }

    private static object EnterPlayer(JsonElement arguments) {
        if (ScreensManager.CurrentScreen is not PlayerScreen playerScreen) {
            throw new InvalidOperationException(
                $"player.enter requires PlayerScreen; current screen is '{ScreensManager.CurrentScreen?.GetType().Name ?? "null"}'.");
        }
        string name = GetString(arguments, "name", "AndroidAutoTest");
        playerScreen.m_nameTextBox.Text = name;
        playerScreen.m_playerData.Name = name;
        if (!playerScreen.TryEnterWorld()) {
            throw new InvalidOperationException("PlayerScreen rejected the enter-world request.");
        }
        return new { playerName = name, accepted = true };
    }

    private static object StartRelayHost() {
        if (GameManager.Project == null || GameManager.IsNetworkProject) {
            throw new InvalidOperationException(
                "Starting a relay host requires a loaded storage-backed world.");
        }
        if (NetworkManager.RelayHostCodeStatus == RelayHostCodeStatus.Failed) {
            NetworkManager.Stop();
        }
        if (!NetworkManager.IsServerRunning) {
            NetworkManager.StartServer();
        }
        if (NetworkManager.RelayHostCodeStatus == RelayHostCodeStatus.Inactive) {
            _ = NetworkManager.StartRelayHostAsync();
        }
        return BuildState();
    }

    private static object ListMods() => new {
        mods = ModsManager.ModListAll
            .Where(mod => mod.modInfo != null)
            .Select(mod => new {
                packageName = mod.modInfo.PackageName ?? string.Empty,
                name = mod.modInfo.Name ?? string.Empty,
                version = mod.modInfo.Version ?? string.Empty,
                apiVersion = mod.modInfo.ApiVersion ?? string.Empty,
                scVersion = mod.modInfo.ScVersion ?? string.Empty,
                disabled = mod.IsDisabled,
                disableReason = mod.DisableReason.ToString(),
                systemMod = mod.IsSystemMod,
                fileName = string.IsNullOrEmpty(mod.ModFilePath)
                    ? string.Empty
                    : Storage.GetFileName(mod.ModFilePath),
                size = mod.Size,
                loaderCount = mod.Loaders?.Count ?? 0
            })
            .OrderBy(mod => mod.packageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.version, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };

    private static object BuildWidgetTree() {
        Widget root = GetAutomationRoot()
            ?? throw new InvalidOperationException("No active screen or dialog.");
        List<WidgetState> widgets = [];
        CollectWidgets(root, root.GetType().Name, widgets);
        return new {
            rootType = root.GetType().Name,
            truncated = widgets.Count >= MaximumWidgetCount,
            widgets
        };
    }

    private static object ClickWidget(JsonElement arguments) {
        string query = GetRequiredString(arguments, "query");
        Widget root = GetAutomationRoot()
            ?? throw new InvalidOperationException("No active screen or dialog.");
        Widget target = FindWidget(root, query)
            ?? throw new InvalidOperationException(
                $"No visible enabled widget named or labelled '{query}' was found in {root.GetType().Name}.");
        ClickableWidget clickable = FindClickableWidget(target)
            ?? throw new InvalidOperationException(
                $"Widget '{query}' ({target.GetType().Name}) is not clickable.");
        InjectClick(clickable);
        return new {
            query,
            matchedName = target.Name ?? string.Empty,
            matchedType = target.GetType().Name,
            matchedText = GetWidgetText(target)
        };
    }

    private static object SetWidgetText(JsonElement arguments) {
        string query = GetRequiredString(arguments, "query");
        string text = GetString(arguments, "text", string.Empty);
        Widget root = GetAutomationRoot()
            ?? throw new InvalidOperationException("No active screen or dialog.");
        Widget target = FindWidget(root, query)
            ?? throw new InvalidOperationException(
                $"No visible enabled widget named or labelled '{query}' was found in {root.GetType().Name}.");
        if (target is not TextBoxWidget textBox) {
            throw new InvalidOperationException(
                $"Widget '{query}' ({target.GetType().Name}) is not a TextBoxWidget.");
        }
        textBox.Text = text;
        return new { query, text };
    }

    private static void ReturnToMainMenu(bool saveWorld) {
        if (saveWorld && GameManager.Project != null && !GameManager.IsNetworkProject) {
            GameManager.SaveProject(waitForCompletion: true, showErrorDialog: false);
        }
        NetworkManager.Stop();
        GameManager.DisposeProject();
        DialogsManager.HideAllDialogs();
        if (LoadingScreen.m_isContentLoaded
            && ScreensManager.CurrentScreen is not MainMenuScreen) {
            ScreensManager.SwitchScreen("MainMenu");
        }
    }

    private static Widget? GetAutomationRoot() {
        if (DialogsManager.Dialogs.Count > 0) {
            return DialogsManager.Dialogs[DialogsManager.Dialogs.Count - 1];
        }
        return ScreensManager.CurrentScreen;
    }

    private static Widget? FindWidget(Widget? root, string query) {
        if (root == null || !root.IsVisible || !root.IsEnabled) {
            return null;
        }
        if (string.Equals(root.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetWidgetText(root), query, StringComparison.OrdinalIgnoreCase)) {
            return root;
        }
        if (root is ContainerWidget container) {
            foreach (Widget child in container.Children) {
                Widget? found = FindWidget(child, query);
                if (found != null) {
                    return found;
                }
            }
        }
        return null;
    }

    private static ClickableWidget? FindClickableWidget(Widget? widget) {
        if (widget == null) return null;
        if (widget is ClickableWidget clickableWidget) return clickableWidget;
        if (widget is BevelledButtonWidget bevelledButtonWidget) return bevelledButtonWidget.m_clickableWidget;
        if (widget is BitmapButtonWidget bitmapButtonWidget) return bitmapButtonWidget.m_clickableWidget;
        if (widget is CheckboxWidget checkboxWidget) return checkboxWidget.m_clickableWidget;
        if (widget is LinkWidget linkWidget) return linkWidget.m_clickableWidget;
        if (widget is ContainerWidget containerWidget) {
            ClickableWidget? childClickable = containerWidget.Children.Find<ClickableWidget>(false);
            if (childClickable != null) return childClickable;
        }
        for (ContainerWidget? parent = widget.ParentWidget; parent != null; parent = parent.ParentWidget) {
            if (parent is BevelledButtonWidget parentBevelled) return parentBevelled.m_clickableWidget;
            if (parent is BitmapButtonWidget parentBitmap) return parentBitmap.m_clickableWidget;
            if (parent is CheckboxWidget parentCheckbox) return parentCheckbox.m_clickableWidget;
            if (parent is LinkWidget parentLink) return parentLink.m_clickableWidget;
        }
        return null;
    }

    private static void InjectClick(ClickableWidget clickableWidget) {
        clickableWidget.IsPressed = false;
        clickableWidget.IsTapped = true;
        clickableWidget.IsClicked = true;
        if (clickableWidget.IsAutoCheckingEnabled) {
            clickableWidget.IsChecked = !clickableWidget.IsChecked;
        }
    }

    private static void CollectWidgets(Widget widget, string path, List<WidgetState> output) {
        if (output.Count >= MaximumWidgetCount || !widget.IsVisible) {
            return;
        }
        output.Add(new WidgetState(
            path,
            widget.GetType().Name,
            widget.Name ?? string.Empty,
            GetWidgetText(widget),
            widget.IsEnabled,
            FindClickableWidget(widget) != null));
        if (widget is not ContainerWidget container) {
            return;
        }
        for (int index = 0; index < container.Children.Count; index++) {
            Widget child = container.Children[index];
            string segment = !string.IsNullOrWhiteSpace(child.Name)
                ? child.Name
                : $"{child.GetType().Name}[{index}]";
            CollectWidgets(child, $"{path}/{segment}", output);
            if (output.Count >= MaximumWidgetCount) return;
        }
    }

    private static string GetWidgetText(Widget widget) => widget switch {
        ButtonWidget button => button.Text ?? string.Empty,
        LabelWidget label => label.Text ?? string.Empty,
        TextBoxWidget textBox => textBox.Text ?? string.Empty,
        CheckboxWidget checkbox => checkbox.Text ?? string.Empty,
        _ => string.Empty
    };

    private static void EnsureContentLoaded() {
        if (!LoadingScreen.m_isContentLoaded) {
            throw new InvalidOperationException("Game content is not loaded yet.");
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
        if (element.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty property in element.EnumerateObject()) {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string GetRequiredString(JsonElement arguments, string name) {
        string value = GetString(arguments, name, string.Empty);
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException($"Argument '{name}' is required.");
        }
        return value;
    }

    private static string GetString(JsonElement arguments, string name, string defaultValue) {
        if (!TryGetProperty(arguments, name, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.Undefined) {
            return defaultValue;
        }
        if (value.ValueKind != JsonValueKind.String) {
            throw new ArgumentException($"Argument '{name}' must be a string.");
        }
        return value.GetString() ?? defaultValue;
    }

    private static bool GetBoolean(JsonElement arguments, string name, bool defaultValue) {
        if (!TryGetProperty(arguments, name, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.Undefined) {
            return defaultValue;
        }
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
            throw new ArgumentException($"Argument '{name}' must be a boolean.");
        }
        return value.GetBoolean();
    }

    private void WriteAtomic(string path, object value) {
        string temporaryPath = $"{path}.tmp";
        Storage.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(value, JsonOptions));
        Storage.MoveFile(temporaryPath, path);
    }

    private static bool IsSafeId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 96
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-');

    private static readonly string[] SupportedCommands = [
        "system.ping",
        "state.get",
        "settings.set",
        "world.list",
        "world.create",
        "world.play",
        "world.exit",
        "world.delete",
        "player.enter",
        "network.startRelayHost",
        "mods.list",
        "ui.tree",
        "ui.click",
        "ui.setText"
    ];
}

public sealed class AndroidAutomationRequest {
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public JsonElement Arguments { get; set; }
}

public sealed record AndroidAutomationResponse(
    int SchemaVersion,
    string Id,
    string Command,
    bool Ok,
    DateTimeOffset CompletedUtc,
    object? Data,
    AndroidAutomationError? Error) {
    public static AndroidAutomationResponse Success(
        AndroidAutomationRequest request,
        object? data) =>
        new(
            AndroidDeviceTestAutomationModLoader.ProtocolVersion,
            request.Id,
            request.Command,
            true,
            DateTimeOffset.UtcNow,
            data,
            null);

    public static AndroidAutomationResponse Failure(
        string id,
        string command,
        Exception exception) =>
        new(
            AndroidDeviceTestAutomationModLoader.ProtocolVersion,
            id,
            command,
            false,
            DateTimeOffset.UtcNow,
            null,
            new AndroidAutomationError(
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? string.Empty));
}

public sealed record AndroidAutomationError(
    string Type,
    string Message,
    string StackTrace);

public sealed record WidgetState(
    string Path,
    string Type,
    string Name,
    string Text,
    bool Enabled,
    bool Clickable);
