using System.Collections;
using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

public static class NeorxnaCompatNetRuntime {
    private const string RecipeDialogTypeName = "Neorxna.NUI.NUIRecipaediaRecipesDialog";
    public const string TargetAssemblyName = "neorxna.dll";

    private const string DebugDrawingTypeName = "Neorxna.Components.ComponentDebugDrawing";
    private const string NeorxnaSubsystemTypeName = "Neorxna.Common.SubsystemNeorxnaBlock";
    private const double PublishInterval = 0.25;
    private const double FullRefreshInterval = 5.0;
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool TryQueueSetHome(Component component, string name) {
        if (!TryGetLocalPlayer(component, out ComponentPlayer player)
            || string.IsNullOrWhiteSpace(name)
            || name.Length > NeorxnaCompatNetPayloads.MaximumHomeNameLength) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.NeorxnaAdapterId,
            (ushort)NeorxnaCompatNetOperation.SetHomeRequest,
            NeorxnaCompatNetPayloads.WriteHomeRequest(new NeorxnaHomeRequest {
                Name = name
            }));
    }

    public static bool TryQueueDeleteHome(Component component, string name) {
        if (!TryGetLocalPlayer(component, out ComponentPlayer player)
            || string.IsNullOrWhiteSpace(name)
            || name.Length > NeorxnaCompatNetPayloads.MaximumHomeNameLength) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.NeorxnaAdapterId,
            (ushort)NeorxnaCompatNetOperation.DeleteHomeRequest,
            NeorxnaCompatNetPayloads.WriteHomeRequest(new NeorxnaHomeRequest {
                Name = name
            }));
    }

    public static bool TryQueueSetBackPosition(Component component) {
        if (!TryGetLocalPlayer(component, out ComponentPlayer player)) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.NeorxnaAdapterId,
            (ushort)NeorxnaCompatNetOperation.SetBackPositionRequest,
            []);
    }

    public static void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (!NetworkManager.IsServerRunning
            || packet?.Payload == null
            || playerData?.ComponentPlayer == null) {
            return;
        }

        Component target = FindDebugDrawing(playerData.ComponentPlayer);
        if (target == null) {
            return;
        }

        switch ((NeorxnaCompatNetOperation)packet.Operation) {
            case NeorxnaCompatNetOperation.SetHomeRequest:
            case NeorxnaCompatNetOperation.DeleteHomeRequest: {
                NeorxnaHomeRequest request = NeorxnaCompatNetPayloads.ReadHomeRequest(packet.Payload);
                string methodName = packet.Operation == (ushort)NeorxnaCompatNetOperation.SetHomeRequest
                    ? "SetHome"
                    : "DeleteHome";
                TryInvoke(FindMethod(target.GetType(), methodName, 1), target, [request.Name]);
                break;
            }
            case NeorxnaCompatNetOperation.SetBackPositionRequest:
                if (packet.Payload.Length == 0) {
                    TryInvoke(FindMethod(target.GetType(), "SetBackPosition", 0), target, []);
                }
                break;
        }
    }

    public static void Publish(
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        Dictionary<string, byte[]> lastPayloads) {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null) {
            return;
        }

        double now = Time.RealTime;
        if (now < nextPublishTime) {
            return;
        }
        nextPublishTime = now + PublishInterval;
        bool fullRefresh = now >= nextFullRefreshTime;
        if (fullRefresh) {
            nextFullRefreshTime = now + FullRefreshInterval;
        }

        Dictionary<int, NeorxnaPlayerHomeState> states = CaptureStates();
        List<NeorxnaPlayerHomeState> changed = [];
        foreach ((int playerIndex, NeorxnaPlayerHomeState state) in states) {
            byte[] payload = NeorxnaCompatNetPayloads.Write(
                new NeorxnaPlayerHomeStateBatch {
                    Sequence = 0,
                    FullRefresh = false,
                    Records = [state]
                });
            string key = playerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (fullRefresh
                || !lastPayloads.TryGetValue(key, out byte[] previous)
                || !previous.AsSpan().SequenceEqual(payload)) {
                lastPayloads[key] = payload;
                changed.Add(state);
            }
        }

        if (changed.Count == 0) {
            return;
        }

        sequence = NextSequence(sequence);
        QueueBatches(changed, sequence, fullRefresh);
    }

    public static void Apply(byte[] payload, ref int lastAppliedSequence) {
        NeorxnaPlayerHomeStateBatch batch;
        try {
            batch = NeorxnaCompatNetPayloads.Read(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))) {
            return;
        }

        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        foreach (NeorxnaPlayerHomeState state in batch.Records) {
            if (state == null || state.PlayerIndex < 0) {
                continue;
            }
            ApplySubsystemState(state);
            ApplyPlayerState(state);
        }
    }

    public static void FixRecipeDialogParent(ref ContainerWidget parentWidget, Dialog dialog) {
        if (!string.Equals(
                dialog?.GetType().FullName,
                RecipeDialogTypeName,
                StringComparison.Ordinal)) {
            return;
        }

        ComponentPlayer mainPlayer = GameManager.Project?
            .FindSubsystem<SubsystemPlayers>()?
            .MainPlayer;
        if (mainPlayer?.GuiWidget != null) {
            parentWidget = mainPlayer.GuiWidget;
        }
    }

    private static Dictionary<int, NeorxnaPlayerHomeState> CaptureStates() {
        Dictionary<int, NeorxnaPlayerHomeState> states = [];
        object subsystem = FindSubsystem(NeorxnaSubsystemTypeName);
        CaptureDictionaryStates(
            GetMemberValue(subsystem, "PlayerBackPositions"),
            states,
            isBackPosition: true);
        CaptureDictionaryStates(
            GetMemberValue(subsystem, "PlayerHomes"),
            states,
            isBackPosition: false);

        foreach (Entity entity in GameManager.Project.Entities) {
            ComponentPlayer player = entity?.FindComponent<ComponentPlayer>();
            Component debugDrawing = FindDebugDrawing(entity);
            if (player?.PlayerData == null || debugDrawing == null) {
                continue;
            }

            int playerIndex = player.PlayerData.PlayerIndex;
            if (playerIndex < 0) {
                continue;
            }
            if (!states.TryGetValue(playerIndex, out NeorxnaPlayerHomeState state)) {
                state = new NeorxnaPlayerHomeState {
                    PlayerIndex = playerIndex
                };
                states[playerIndex] = state;
            }

            object homes = TryInvoke(
                FindMethod(debugDrawing.GetType(), "ExportAllHomes", 0),
                debugDrawing,
                [],
                out object homesResult)
                ? homesResult
                : null;
            if (homes is IDictionary homeDictionary) {
                state.Homes = ReadHomes(homeDictionary);
            }

            PropertyInfo backPosition = FindProperty(debugDrawing.GetType(), "BackPosition");
            if (backPosition != null) {
                try {
                    object value = backPosition.GetValue(debugDrawing);
                    if (value is Vector3 vector) {
                        state.HasBackPosition = true;
                        state.BackPosition = vector;
                    }
                }
                catch {
                }
            }
        }

        return states;
    }

    private static void CaptureDictionaryStates(
        object dictionaryObject,
        Dictionary<int, NeorxnaPlayerHomeState> states,
        bool isBackPosition) {
        if (dictionaryObject is not IDictionary dictionary) {
            return;
        }

        foreach (DictionaryEntry entry in dictionary) {
            if (entry.Key is not int playerIndex || playerIndex < 0) {
                continue;
            }
            if (!states.TryGetValue(playerIndex, out NeorxnaPlayerHomeState state)) {
                state = new NeorxnaPlayerHomeState {
                    PlayerIndex = playerIndex
                };
                states[playerIndex] = state;
            }

            if (isBackPosition && entry.Value is Vector3 backPosition) {
                state.HasBackPosition = true;
                state.BackPosition = backPosition;
            }
            else if (!isBackPosition && entry.Value is IDictionary homes) {
                state.Homes = ReadHomes(homes);
            }
        }
    }

    private static List<NeorxnaHomeEntry> ReadHomes(IDictionary dictionary) {
        List<NeorxnaHomeEntry> homes = [];
        foreach (DictionaryEntry entry in dictionary) {
            if (entry.Key is not string name
                || entry.Value is not Vector3 position
                || string.IsNullOrWhiteSpace(name)
                || name.Length > NeorxnaCompatNetPayloads.MaximumHomeNameLength
                || !NeorxnaCompatNetPayloads.IsFinite(position)) {
                continue;
            }
            homes.Add(new NeorxnaHomeEntry {
                Name = name,
                Position = position
            });
            if (homes.Count >= NeorxnaCompatNetPayloads.MaximumHomesPerPlayer) {
                break;
            }
        }
        return homes;
    }

    private static void ApplySubsystemState(NeorxnaPlayerHomeState state) {
        object subsystem = FindSubsystem(NeorxnaSubsystemTypeName);
        if (subsystem == null) {
            return;
        }

        if (GetMemberValue(subsystem, "PlayerBackPositions") is IDictionary backPositions) {
            if (state.HasBackPosition) {
                backPositions[state.PlayerIndex] = state.BackPosition;
            }
            else {
                backPositions.Remove(state.PlayerIndex);
            }
        }

        if (GetMemberValue(subsystem, "PlayerHomes") is IDictionary playerHomes) {
            Dictionary<string, Vector3> homes = [];
            foreach (NeorxnaHomeEntry home in state.Homes) {
                if (home != null) {
                    homes[home.Name] = home.Position;
                }
            }
            playerHomes[state.PlayerIndex] = homes;
        }
    }

    private static void ApplyPlayerState(NeorxnaPlayerHomeState state) {
        foreach (Entity entity in GameManager.Project?.Entities?.ToArray() ?? Array.Empty<Entity>()) {
            ComponentPlayer player = entity?.FindComponent<ComponentPlayer>();
            if (player?.PlayerData?.PlayerIndex != state.PlayerIndex) {
                continue;
            }

            Component debugDrawing = FindDebugDrawing(entity);
            if (debugDrawing == null) {
                return;
            }

            Dictionary<string, Vector3> homes = [];
            foreach (NeorxnaHomeEntry home in state.Homes) {
                if (home != null) {
                    homes[home.Name] = home.Position;
                }
            }
            TryInvoke(
                FindMethod(debugDrawing.GetType(), "ImportAllHomes", 1),
                debugDrawing,
                [homes]);

            PropertyInfo backPosition = FindProperty(debugDrawing.GetType(), "BackPosition");
            if (backPosition?.CanWrite == true) {
                try {
                    object value = state.HasBackPosition
                        ? (backPosition.PropertyType == typeof(Vector3?)
                            ? Activator.CreateInstance(typeof(Vector3?), state.BackPosition)
                            : state.BackPosition)
                        : null;
                    backPosition.SetValue(debugDrawing, value);
                }
                catch {
                }
            }
            return;
        }
    }

    private static Component FindDebugDrawing(Entity entity) {
        return entity?.Components?.FirstOrDefault(component =>
            component != null
            && string.Equals(
                component.GetType().FullName,
                DebugDrawingTypeName,
                StringComparison.Ordinal));
    }

    private static Component FindDebugDrawing(ComponentPlayer player) {
        return FindDebugDrawing(player?.Entity);
    }

    private static bool TryGetLocalPlayer(Component component, out ComponentPlayer player) {
        player = component?.Entity?.FindComponent<ComponentPlayer>();
        return NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning
            && player?.PlayerData?.IsMainPlayer == true;
    }

    private static object FindSubsystem(string fullName) {
        return GameManager.Project?.Subsystems?.FirstOrDefault(subsystem =>
            subsystem != null
            && string.Equals(
                subsystem.GetType().FullName,
                fullName,
                StringComparison.Ordinal));
    }

    private static object GetMemberValue(object instance, string name) {
        if (instance == null) {
            return null;
        }
        Type type = instance.GetType();
        FieldInfo field = FindField(type, name);
        try {
            if (field != null) {
                return field.GetValue(instance);
            }
            PropertyInfo property = FindProperty(type, name);
            return property?.GetValue(instance);
        }
        catch {
            return null;
        }
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            FieldInfo field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        return null;
    }

    private static PropertyInfo FindProperty(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            PropertyInfo property = current.GetProperty(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (property != null) {
                return property;
            }
        }
        return null;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            foreach (MethodInfo method in current.GetMethods(InstanceFlags | BindingFlags.DeclaredOnly)) {
                if ((method.Name == name || method.Name.EndsWith("." + name, StringComparison.Ordinal))
                    && method.GetParameters().Length == parameterCount) {
                    return method;
                }
            }
        }
        return null;
    }

    private static bool TryInvoke(
        MethodInfo method,
        object instance,
        object[] arguments) {
        return TryInvoke(method, instance, arguments, out _);
    }

    private static bool TryInvoke(
        MethodInfo method,
        object instance,
        object[] arguments,
        out object result) {
        result = null;
        if (method == null) {
            return false;
        }
        try {
            result = method.Invoke(instance, arguments);
            return true;
        }
        catch {
            return false;
        }
    }

    private static void QueueBatches(
        List<NeorxnaPlayerHomeState> records,
        int sequence,
        bool fullRefresh) {
        List<NeorxnaPlayerHomeState> current = [];
        foreach (NeorxnaPlayerHomeState record in records) {
            current.Add(record);
            byte[] payload = NeorxnaCompatNetPayloads.Write(
                new NeorxnaPlayerHomeStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = current
                });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueBatch(current, sequence, fullRefresh);
            }
            current = [record];
        }

        if (current.Count > 0) {
            QueueBatch(current, sequence, fullRefresh);
        }
    }

    private static void QueueBatch(
        List<NeorxnaPlayerHomeState> records,
        int sequence,
        bool fullRefresh) {
        byte[] payload = NeorxnaCompatNetPayloads.Write(
            new NeorxnaPlayerHomeStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Records = records
            });
        if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
            CompatNetCore.QueueBroadcast(
                ScopedUpdateGuardCompatNetAdapter.NeorxnaAdapterId,
                (ushort)NeorxnaCompatNetOperation.HomeStateSnapshot,
                payload);
        }
    }

    private static int NextSequence(int sequence) {
        return sequence == int.MaxValue ? 1 : sequence + 1;
    }

    private static bool IsSequenceNewer(int sequence, int previous) {
        if (previous == 0) {
            return sequence > 0;
        }
        if (sequence == previous) {
            return false;
        }
        if (previous == int.MaxValue) {
            return sequence == 1;
        }
        if (previous < int.MaxValue / 2 && sequence > int.MaxValue / 2) {
            return false;
        }
        return sequence > previous;
    }

    private static bool IsSequenceAtLeast(int sequence, int previous) {
        return sequence == previous || IsSequenceNewer(sequence, previous);
    }
}
