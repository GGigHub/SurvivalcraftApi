using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Runtime bridge for the Cecil-patched Elytron assembly. Elytron remains an
/// optional dependency: target objects are accessed through object/reflection.
/// </summary>
public static class ElytronCompatNetRuntime {
    private const string ComponentGlidingTypeName = "Elytron.ComponentGliding";
    private const string GlidingStatePropertyName = "IsGlidingActive";
    private const string GlidingStateSetterName = "set_IsGlidingActive";
    private const string FireworksMethodName = "FireworksFly";
    private const string LastGlideYawFieldName = "m_lastGlideYaw";
    private const string LastActiveIndexFieldName = "m_lastActiveIndex";
    private const string LastFireworksCountFieldName = "m_lastFireworksCount";
    private const int FireworksBlockId = 215;
    private const int RequiredClothingId = 322;
    private const float MinHeightFromGround = 3f;
    private const double DoubleJumpTimeThreshold = 0.3;
    private const double MinimumJumpPacketInterval = 0.02;
    private const double MinimumFireworksPacketInterval = 0.08;

    private sealed class ClientState {
        public bool LastHardwareJump;
        public bool LastLocomotionJump;
        public int NextSequence;
        public int NextFireworkSequence;
    }

    private sealed class ServerJumpState {
        public bool HasPreviousJump;
        public double LastJumpTime;
        public int LastSequence;
    }

    private static readonly ConditionalWeakTable<object, ClientState> s_clientStates = new();
    private static readonly Dictionary<int, ServerJumpState> s_serverJumpStates = [];
    private static readonly Dictionary<int, int> s_lastFireworkSequences = [];
    private static readonly Dictionary<int, double> s_lastJumpPacketTimes = [];
    private static readonly Dictionary<int, double> s_lastFireworksPacketTimes = [];
    private static int s_missingTargetWarningLogged;

    /// <summary>
    /// Existing remote-update guard. A client never simulates Elytron's local
    /// input/physics update for another player's entity.
    /// </summary>
    public static bool ShouldSkipGlidingUpdate(object glidingComponent) {
        if (!IsClientOnly()
            || glidingComponent is not Component component
            || component.Entity == null) {
            return false;
        }

        ComponentPlayer player = component.Entity.FindComponent<ComponentPlayer>();
        return player?.PlayerData != null && !player.PlayerData.IsMainPlayer;
    }

    /// <summary>
    /// Captures the same jump edges that Elytron.Update consumes. The request is
    /// only the edge plus a local sequence; no entity, time, velocity, or state is
    /// accepted from the client.
    /// </summary>
    public static void CaptureJumpIntent(object glidingComponent) {
        if (!IsClientOnly()
            || !CompatNetCore.NetworkBridgeReady
            || glidingComponent is not Component component
            || component.Entity == null) {
            return;
        }

        ComponentPlayer player = component.Entity.FindComponent<ComponentPlayer>();
        if (player?.PlayerData?.IsMainPlayer != true) {
            return;
        }

        ClientState state = s_clientStates.GetOrCreateValue(glidingComponent);
        bool hardwareJump = player.ComponentInput?.PlayerInput.Jump == true;
        bool locomotionJump = player.ComponentLocomotion?.JumpOrder > 0f;
        bool edge = (hardwareJump && !state.LastHardwareJump)
            || (locomotionJump && !state.LastLocomotionJump);
        state.LastHardwareJump = hardwareJump;
        state.LastLocomotionJump = locomotionJump;

        if (!edge) {
            return;
        }

        int sequence = NextSequence(ref state.NextSequence);
        ElytronJumpPressedRequest request = new() { Sequence = sequence };
        CompatNetCore.QueueRequest(
            player,
            ElytronCompatNetAdapter.StableAdapterId,
            (ushort)ElytronCompatNetOperation.JumpPressed,
            CompatNetPayload.Write(request.Serialize));
    }

    /// <summary>
    /// Replaces Elytron's private-property writes. On a network client the target
    /// state is not changed locally; the authoritative server toggles it after two
    /// authenticated jump edges. Offline and listen-server behavior retains the
    /// original setter.
    /// </summary>
    public static void SetGlidingActiveBridge(object glidingComponent, bool requestedActive) {
        if (glidingComponent == null) {
            return;
        }

        if (IsClientOnly() && CompatNetCore.NetworkBridgeReady) {
            return;
        }

        InvokeOriginalSetter(glidingComponent, requestedActive);
    }

    /// <summary>
    /// Replaces the only source call to FireworksFly. The client sends a sequence
    /// only; the server derives the slot and consumes a one-time count-decrease
    /// token from the target component's own state.
    /// </summary>
    public static void FireworksFlyBridge(object glidingComponent) {
        if (glidingComponent == null) {
            return;
        }

        ComponentPlayer player = ResolvePlayer(glidingComponent);
        if (NetworkManager.IsServerRunning) {
            TryExecuteServerFireworks(player, glidingComponent, sequence: null);
            return;
        }

        if (!IsClientOnly()) {
            InvokeOriginalFireworks(glidingComponent);
            return;
        }

        if (!CompatNetCore.NetworkBridgeReady) {
            InvokeOriginalFireworks(glidingComponent);
            return;
        }

        if (player?.PlayerData?.IsMainPlayer != true) {
            return;
        }

        ClientState state = s_clientStates.GetOrCreateValue(glidingComponent);
        ElytronFireworkIntentRequest request = new() {
            Sequence = NextSequence(ref state.NextFireworkSequence)
        };
        CompatNetCore.QueueRequest(
            player,
            ElytronCompatNetAdapter.StableAdapterId,
            (ushort)ElytronCompatNetOperation.FireworkIntent,
            CompatNetPayload.Write(request.Serialize));
    }

    /// <summary>
    /// Applies one authenticated jump edge. The server uses receive time to
    /// reproduce Elytron's 0.3-second double-jump rule and toggles only the
    /// requesting player's own component.
    /// </summary>
    public static bool TryExecuteServerJump(ComponentPlayer requester, int sequence) {
        if (!NetworkManager.IsServerRunning
            || requester?.PlayerData == null
            || requester.Entity == null
            || sequence <= 0
            || !AcceptJumpPacket(requester.PlayerData.PlayerIndex, sequence)) {
            return false;
        }

        object glidingComponent = FindGlidingComponent(requester);
        if (glidingComponent == null) {
            return false;
        }

        ServerJumpState state = GetServerJumpState(requester.PlayerData.PlayerIndex);
        double now = Time.RealTime;
        bool isDoubleJump = state.HasPreviousJump
            && now - state.LastJumpTime < DoubleJumpTimeThreshold;
        state.HasPreviousJump = true;
        state.LastJumpTime = now;

        if (!isDoubleJump) {
            return false;
        }

        state.HasPreviousJump = false;
        if (ReadBooleanProperty(glidingComponent, GlidingStatePropertyName) == true) {
            bool stopped = InvokeOriginalSetter(glidingComponent, false);
            if (stopped) {
                QueueAuthoritativeBodyUpdate(requester);
            }
            return stopped;
        }

        if (!CanStartGliding(requester, glidingComponent)) {
            return false;
        }

        if (!InvokeOriginalSetter(glidingComponent, true)
            || !TrySetLastGlideYaw(glidingComponent, requester.ComponentBody.Rotation)) {
            // Do not leave a partially initialized target state if the versioned
            // private yaw field is unavailable.
            InvokeOriginalSetter(glidingComponent, false);
            return false;
        }

        QueueAuthoritativeBodyUpdate(requester);
        return true;
    }

    /// <summary>
    /// Validates a firework intent against server inventory and the target's own
    /// observed count decrease. This prevents replaying a single item-use request.
    /// </summary>
    public static bool TryExecuteServerFireworks(
        ComponentPlayer requester,
        int sequence) {
        return TryExecuteServerFireworks(
            requester,
            FindGlidingComponent(requester),
            sequence);
    }

    private static bool TryExecuteServerFireworks(
        ComponentPlayer requester,
        object glidingComponent,
        int? sequence) {
        if (!NetworkManager.IsServerRunning
            || requester?.PlayerData == null
            || requester.Entity == null
            || glidingComponent == null
            || !AcceptFireworksPacket(requester.PlayerData.PlayerIndex, sequence)
            || requester.ComponentMiner?.Inventory == null
            || requester.ComponentBody == null
            || requester.ComponentHealth?.Health <= 0f) {
            return false;
        }

        IInventory inventory = requester.ComponentMiner.Inventory;
        int slotIndex = inventory.ActiveSlotIndex;
        if (slotIndex < 0
            || Terrain.ExtractContents(inventory.GetSlotValue(slotIndex)) != FireworksBlockId) {
            return false;
        }

        FieldInfo lastSlotField = FindField(glidingComponent.GetType(), LastActiveIndexFieldName);
        FieldInfo lastCountField = FindField(glidingComponent.GetType(), LastFireworksCountFieldName);
        if (lastSlotField?.FieldType != typeof(int)
            || lastCountField?.FieldType != typeof(int)) {
            WarnMissingTarget(
                $"Target firework token fields {LastActiveIndexFieldName}/{LastFireworksCountFieldName} were not found.");
            return false;
        }

        try {
            int lastSlot = (int)lastSlotField.GetValue(glidingComponent);
            int lastCount = (int)lastCountField.GetValue(glidingComponent);
            int currentCount = inventory.GetSlotCount(slotIndex);
            if (lastSlot != slotIndex || lastCount <= 0 || currentCount >= lastCount) {
                return false;
            }

            if (!InvokeOriginalFireworks(glidingComponent)) {
                return false;
            }

            // Consume the observation token. The normal Elytron.Update will write
            // the same current count later in the frame, so this is idempotent.
            lastCountField.SetValue(glidingComponent, currentCount);
            if (sequence.HasValue) {
                s_lastFireworkSequences[requester.PlayerData.PlayerIndex] = sequence.Value;
            }
            QueueAuthoritativeBodyUpdate(requester);
            return true;
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to consume Elytron firework token: {e}");
            return false;
        }
    }

    internal static void WarnMissingTarget(string message) {
        if (Interlocked.Exchange(ref s_missingTargetWarningLogged, 1) == 0) {
            Log.Warning($"[Survivalcraft.CompatNet] {message}");
        }
    }

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }

    private static int NextSequence(ref int sequence) {
        if (sequence == int.MaxValue) {
            sequence = 1;
        }
        else {
            sequence++;
        }
        return sequence;
    }

    private static ServerJumpState GetServerJumpState(int playerIndex) {
        if (!s_serverJumpStates.TryGetValue(playerIndex, out ServerJumpState state)) {
            state = new ServerJumpState();
            s_serverJumpStates[playerIndex] = state;
        }
        return state;
    }

    private static bool AcceptJumpPacket(int playerIndex, int sequence) {
        double now = Time.RealTime;
        if (s_lastJumpPacketTimes.TryGetValue(playerIndex, out double previous)
            && now - previous < MinimumJumpPacketInterval) {
            return false;
        }

        ServerJumpState state = GetServerJumpState(playerIndex);
        if (sequence <= state.LastSequence) {
            return false;
        }

        state.LastSequence = sequence;
        s_lastJumpPacketTimes[playerIndex] = now;
        return true;
    }

    private static bool AcceptFireworksPacket(int playerIndex, int? sequence) {
        double now = Time.RealTime;
        if (s_lastFireworksPacketTimes.TryGetValue(playerIndex, out double previous)
            && now - previous < MinimumFireworksPacketInterval) {
            return false;
        }

        if (sequence.HasValue
            && s_lastFireworkSequences.TryGetValue(playerIndex, out int previousSequence)
            && sequence.Value <= previousSequence) {
            return false;
        }

        s_lastFireworksPacketTimes[playerIndex] = now;
        return true;
    }

    private static ComponentPlayer ResolvePlayer(object componentObject) {
        return componentObject is Component component && component.Entity != null
            ? component.Entity.FindComponent<ComponentPlayer>()
            : null;
    }

    private static object FindGlidingComponent(ComponentPlayer player) {
        return player?.Entity?.Components.FirstOrDefault(component =>
            string.Equals(
                component.GetType().FullName,
                ComponentGlidingTypeName,
                StringComparison.Ordinal));
    }

    private static bool CanStartGliding(ComponentPlayer player, object glidingComponent) {
        SubsystemGameInfo gameInfo = GameManager.Project?.FindSubsystem<SubsystemGameInfo>();
        if (gameInfo?.WorldSettings == null
            || gameInfo.WorldSettings.GameMode == GameMode.Creative
            || player.ComponentBody == null
            || player.ComponentBody.StandingOnValue.HasValue
            || player.ComponentBody.ImmersionFactor >= 0.9f
            || player.ComponentRider?.Mount != null
            || player.ComponentLocomotion == null
            || player.ComponentLocomotion.IsCreativeFlyEnabled
            || ComponentLocomotion.IsCreativeFlyEnabledSet
            || player.ComponentHealth?.Health <= 0f
            || !HasRequiredClothing(player)
            || !HasRequiredHeight(glidingComponent)
            || ReadBooleanProperty(glidingComponent, GlidingStatePropertyName) != false) {
            return false;
        }

        return true;
    }

    private static bool HasRequiredClothing(ComponentPlayer player) {
        ComponentClothing clothing = player.ComponentClothing;
        if (clothing == null) {
            return false;
        }

        try {
            foreach (ClothingSlot slot in ClothingSlot.ClothingSlots.Values) {
                foreach (int value in clothing.GetClothes(slot)) {
                    if (value == 0) {
                        continue;
                    }

                    ClothingData data = BlocksManager.Blocks[Terrain.ExtractContents(value)]
                        .GetClothingData(value);
                    if (data?.Index == RequiredClothingId) {
                        return true;
                    }
                }
            }
        }
        catch {
            // A missing/invalid clothing table is not an authorization success.
        }

        return false;
    }

    private static bool HasRequiredHeight(object glidingComponent) {
        MethodInfo method = FindMethod(glidingComponent.GetType(), "GetHeightFromGround", 0);
        if (method == null) {
            return false;
        }

        try {
            return method.Invoke(glidingComponent, null) is float height
                && height >= MinHeightFromGround;
        }
        catch {
            return false;
        }
    }

    private static bool TrySetLastGlideYaw(object glidingComponent, Quaternion rotation) {
        FieldInfo field = FindField(glidingComponent.GetType(), LastGlideYawFieldName);
        if (field?.FieldType != typeof(float)) {
            WarnMissingTarget($"Target field {LastGlideYawFieldName} was not found.");
            return false;
        }

        try {
            field.SetValue(glidingComponent, rotation.ToYawPitchRoll().X);
            return true;
        }
        catch {
            return false;
        }
    }

    private static bool InvokeOriginalSetter(object glidingComponent, bool active) {
        MethodInfo method = FindMethod(glidingComponent.GetType(), GlidingStateSetterName, 1);
        if (method == null
            || method.ReturnType != typeof(void)
            || method.GetParameters()[0].ParameterType != typeof(bool)) {
            WarnMissingTarget($"Target method {GlidingStateSetterName}(bool) was not found.");
            return false;
        }

        try {
            method.Invoke(glidingComponent, [active]);
            return true;
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to invoke {GlidingStatePropertyName}: {e}");
            return false;
        }
    }

    private static bool InvokeOriginalFireworks(object glidingComponent) {
        MethodInfo method = FindMethod(glidingComponent.GetType(), FireworksMethodName, 0);
        if (method == null || method.ReturnType != typeof(void)) {
            WarnMissingTarget($"Target method {FireworksMethodName}() was not found.");
            return false;
        }

        try {
            method.Invoke(glidingComponent, null);
            return true;
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to invoke {FireworksMethodName}: {e}");
            return false;
        }
    }

    private static bool? ReadBooleanProperty(object instance, string name) {
        PropertyInfo property = instance.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.PropertyType != typeof(bool) || property.GetMethod == null) {
            return null;
        }

        try {
            return (bool)property.GetValue(instance);
        }
        catch {
            return null;
        }
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null; current = current.BaseType) {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }

        return null;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount) {
        for (Type current = type; current != null; current = current.BaseType) {
            MethodInfo method = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.Name == name
                    && !candidate.IsStatic
                    && candidate.GetParameters().Length == parameterCount);
            if (method != null) {
                return method;
            }
        }

        return null;
    }

    private static void QueueAuthoritativeBodyUpdate(ComponentPlayer player) {
        NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
    }
}
