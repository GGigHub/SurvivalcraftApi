using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Runtime bridge used by the Cecil-patched Neorxna assembly. The target assembly
/// is optional, so all target-specific state is accessed by object/reflection.
/// </summary>
public static class NeorxnaCompatNetRuntime {
    private const string ComponentDashTypeName = "Neorxna.Components.ComponentDash";
    private const string LegacyComponentDashTypeName = "neorxna.ComponentDash";
    private const string DashActiveFieldName = "_1680";
    private const double MinimumRequestInterval = 0.08;

    private sealed class ClientState {
        public bool RequestedActive;
    }

    private sealed class ServerLease {
        public bool Active;
    }

    private static readonly ConditionalWeakTable<object, ClientState> s_clientStates = new();
    private static readonly ConditionalWeakTable<object, ServerLease> s_serverLeases = new();
    private static readonly Dictionary<int, double> s_lastRequestTimes = [];
    private static int s_missingTargetWarningLogged;

    /// <summary>
    /// Existing remote-update guard. Remote players are already driven by the
    /// standard body synchronizer and must not run Neorxna's local input logic.
    /// </summary>
    public static bool ShouldSkipDashUpdate(ComponentPlayer player) {
        if (!IsClientOnly()) {
            return false;
        }

        return player?.PlayerData?.IsMainPlayer != true;
    }

    /// <summary>
    /// Handles writes to ComponentDash._1680 from Update. A client converts only
    /// the source mod's boolean state transitions into start/stop intent packets;
    /// it does not set the target dash field locally.
    /// </summary>
    public static void SetDashActiveBridge(object dashComponent, bool requestedActive) {
        if (dashComponent == null) {
            return;
        }

        if (!IsClientOnly()) {
            if (IsServerLeaseActive(dashComponent) && !CanContinueDash(dashComponent)) {
                ReleaseServerLease(dashComponent);
                SetRawDashActive(dashComponent, false);
                return;
            }

            SetRawDashActive(dashComponent, requestedActive);
            return;
        }

        ComponentPlayer player = ResolvePlayer(dashComponent);
        if (player?.PlayerData?.IsMainPlayer != true) {
            return;
        }

        ClientState state = s_clientStates.GetOrCreateValue(dashComponent);
        if (requestedActive) {
            if (state.RequestedActive) {
                return;
            }

            if (QueueClientIntent(player, NeorxnaCompatNetOperation.StartDash)) {
                state.RequestedActive = true;
            }
            return;
        }

        if (!state.RequestedActive) {
            return;
        }

        if (QueueClientIntent(player, NeorxnaCompatNetOperation.StopDash)) {
            state.RequestedActive = false;
        }
    }

    /// <summary>
    /// Handles the active-field write inside StopDashing. It is kept separate from
    /// Update's ordinary false assignment so disabling the Neorxna setting or any
    /// other original StopDashing path can always revoke the server lease.
    /// </summary>
    public static void ClearDashActiveBridge(object dashComponent) {
        if (dashComponent == null) {
            return;
        }

        if (IsClientOnly()) {
            ComponentPlayer player = ResolvePlayer(dashComponent);
            if (player?.PlayerData?.IsMainPlayer == true) {
                ClientState state = s_clientStates.GetOrCreateValue(dashComponent);
                if (state.RequestedActive
                    && QueueClientIntent(player, NeorxnaCompatNetOperation.StopDash)) {
                    state.RequestedActive = false;
                }
            }
            return;
        }

        ReleaseServerLease(dashComponent);
        SetRawDashActive(dashComponent, false);
    }

    /// <summary>
    /// Validates and applies a start/stop intent to the authenticated player's own
    /// ComponentDash. The packet contains no entity ID, direction, speed, stamina,
    /// or position supplied by the client.
    /// </summary>
    public static bool TryExecuteServerDash(ComponentPlayer requester, bool active) {
        if (!NetworkManager.IsServerRunning
            || requester?.PlayerData == null
            || requester.Entity == null
            || !AcceptRequest(requester.PlayerData.PlayerIndex)) {
            return false;
        }

        object dashComponent = FindDashComponent(requester);
        if (dashComponent == null) {
            return false;
        }

        if (!active) {
            ReleaseServerLease(dashComponent);
            SetRawDashActive(dashComponent, false);
            return true;
        }

        if (!CanStartDash(requester, dashComponent)) {
            return false;
        }

        s_serverLeases.GetOrCreateValue(dashComponent).Active = true;
        SetRawDashActive(dashComponent, true);
        return true;
    }

    public static bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || updateable == null
            || !IsComponentDash(updateable.GetType())) {
            return false;
        }

        if (updateable is not Component component || component.Entity == null) {
            return true;
        }

        return ShouldSkipDashUpdate(component.Entity.FindComponent<ComponentPlayer>());
    }

    internal static void WarnMissingTarget(string message) {
        if (Interlocked.Exchange(ref s_missingTargetWarningLogged, 1) == 0) {
            Log.Warning($"[Survivalcraft.CompatNet] {message}");
        }
    }

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }

    private static ComponentPlayer ResolvePlayer(object dashComponent) {
        return dashComponent is Component component && component.Entity != null
            ? component.Entity.FindComponent<ComponentPlayer>()
            : null;
    }

    private static object FindDashComponent(ComponentPlayer player) {
        return player.Entity.Components.FirstOrDefault(component =>
            string.Equals(component.GetType().FullName, ComponentDashTypeName, StringComparison.Ordinal)
            || string.Equals(component.GetType().FullName, LegacyComponentDashTypeName, StringComparison.Ordinal));
    }

    private static bool QueueClientIntent(
        ComponentPlayer player,
        NeorxnaCompatNetOperation operation) {
        return CompatNetCore.QueueRequest(
            player,
            NeorxnaCompatNetAdapter.StableAdapterId,
            (ushort)operation,
            NeorxnaCompatNetPayloads.Empty());
    }

    private static bool CanStartDash(ComponentPlayer player, object dashComponent) {
        if (ReadBooleanProperty(dashComponent, "IsEnabled") != true
            || player.ComponentBody == null
            || player.ComponentVitalStats == null
            || player.ComponentVitalStats.Food <= 0.3f
            || player.ComponentVitalStats.Stamina <= 0.1f
            || player.ComponentSickness?.IsSick == true
            || player.ComponentFlu?.HasFlu == true
            || player.ComponentBody.ImmersionFactor > 0.1f
            || player.ComponentRider?.Mount != null
            || player.ComponentBody.IsCrouching
            || ReadBooleanField(dashComponent, DashActiveFieldName) == true) {
            return false;
        }

        return player.ComponentHealth == null || player.ComponentHealth.Health > 0f;
    }

    private static bool CanContinueDash(object dashComponent) {
        ComponentPlayer player = ResolvePlayer(dashComponent);
        return player != null
            && ReadBooleanProperty(dashComponent, "IsEnabled") == true
            && player.ComponentVitalStats != null
            && player.ComponentVitalStats.Food > 0.3f
            && player.ComponentVitalStats.Stamina > 0.1f
            && player.ComponentSickness?.IsSick != true
            && player.ComponentFlu?.HasFlu != true
            && player.ComponentBody != null
            && player.ComponentBody.ImmersionFactor <= 0.1f
            && player.ComponentRider?.Mount == null
            && !player.ComponentBody.IsCrouching
            && (player.ComponentHealth == null || player.ComponentHealth.Health > 0f);
    }

    private static bool AcceptRequest(int playerIndex) {
        double now = Time.RealTime;
        if (s_lastRequestTimes.TryGetValue(playerIndex, out double previous)
            && now - previous < MinimumRequestInterval) {
            return false;
        }

        s_lastRequestTimes[playerIndex] = now;
        return true;
    }

    private static bool IsServerLeaseActive(object dashComponent) {
        return s_serverLeases.TryGetValue(dashComponent, out ServerLease lease) && lease.Active;
    }

    private static void ReleaseServerLease(object dashComponent) {
        if (s_serverLeases.TryGetValue(dashComponent, out ServerLease lease)) {
            lease.Active = false;
        }
    }

    private static void SetRawDashActive(object dashComponent, bool active) {
        FieldInfo field = FindField(dashComponent.GetType(), DashActiveFieldName);
        if (field?.FieldType != typeof(bool)) {
            WarnMissingTarget($"Target dash state field {DashActiveFieldName} was not found at runtime.");
            return;
        }

        try {
            field.SetValue(dashComponent, active);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to set Neorxna dash state: {e}");
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

    private static bool? ReadBooleanField(object instance, string name) {
        FieldInfo field = FindField(instance.GetType(), name);
        if (field?.FieldType != typeof(bool)) {
            return null;
        }

        try {
            return (bool)field.GetValue(instance);
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

    private static bool IsComponentDash(Type type) {
        return string.Equals(type.FullName, ComponentDashTypeName, StringComparison.Ordinal)
            || string.Equals(type.FullName, LegacyComponentDashTypeName, StringComparison.Ordinal);
    }
}
