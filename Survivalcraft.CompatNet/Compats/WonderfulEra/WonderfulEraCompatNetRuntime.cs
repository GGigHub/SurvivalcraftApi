using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Relays WonderfulEra's custom vehicle control orders to the server. The
/// vanilla player-body packet contains position data but not this mod's
/// MoveOrder/TurnOrder/FlyOrder values, so the remote vehicle update guard
/// needs this small input channel.
/// </summary>
public static class WonderfulEraCompatNetRuntime {
    public const string TargetAssemblyName = "WonderfulEra.dll";

    private const string VehicleTypeName = "WonderfulEra.ComponentVehicle";
    private const string SubVehicleTypeName = "WonderfulEra.ComponentSubVehicle";
    private const double ClientResendInterval = 0.05;
    private const double ServerControlTimeout = 0.25;
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class PendingControl {
        public int PlayerEntityId;
        public WonderfulEraVehicleControlRequest Request;
        public double ReceivedTime;
    }

    private static readonly Dictionary<int, PendingControl> s_pendingControls = [];
    private static readonly Dictionary<int, byte[]> s_lastClientPayloads = [];
    private static readonly Dictionary<int, double> s_lastClientSendTimes = [];

    public static void Reset() {
        s_pendingControls.Clear();
        s_lastClientPayloads.Clear();
        s_lastClientSendTimes.Clear();
    }

    /// <summary>
    /// Called from WonderfulEra's OnPlayerControlOtherMount hook. On a pure
    /// client it queues the current input while the scoped update guard keeps
    /// the vehicle simulation server-owned. On the server it applies the most
    /// recent authenticated input before the vehicle's authoritative update
    /// runs.
    /// </summary>
    public static bool TrySyncVehicleControl(
        ComponentPlayer player,
        bool skippedByOtherMods,
        ref bool skipVanilla) {
        skipVanilla = false;
        if (player?.Entity == null || player.ComponentRider?.Mount == null) {
            return false;
        }

        Entity vehicleEntity = player.ComponentRider.Mount.Entity;
        if (!TryGetVehicleComponent(vehicleEntity, out Component vehicle, out bool subVehicle)) {
            return false;
        }

        if (NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning) {
            if (player.PlayerData?.IsMainPlayer != true || player.ComponentInput == null) {
                return false;
            }

            PlayerInput input = player.ComponentInput.PlayerInput;
            WonderfulEraVehicleControlRequest request = new() {
                VehicleEntityId = vehicleEntity.Id,
                SubVehicle = subVehicle,
                MoveOrder = input.Move.Z,
                TurnOrder = input.Move.X,
                FlyOrderY = input.Move.Y
            };
            byte[] payload = WonderfulEraCompatNetPayloads.WriteVehicleControlRequest(request);
            double now = Time.RealTime;
            if (s_lastClientPayloads.TryGetValue(vehicleEntity.Id, out byte[] previous)
                && previous.AsSpan().SequenceEqual(payload)
                && s_lastClientSendTimes.TryGetValue(vehicleEntity.Id, out double previousTime)
                && now < previousTime + ClientResendInterval) {
                return false;
            }

            if (!CompatNetCore.QueueRequest(
                    player,
                    ScopedUpdateGuardCompatNetAdapter.WonderfulEraAdapterId,
                    (ushort)WonderfulEraCompatNetOperation.VehicleControlRequest,
                    payload)) {
                return false;
            }

            s_lastClientPayloads[vehicleEntity.Id] = payload;
            s_lastClientSendTimes[vehicleEntity.Id] = now;
            return false;
        }

        if (!NetworkManager.IsServerRunning
            || !s_pendingControls.TryGetValue(vehicleEntity.Id, out PendingControl pending)
            || pending.PlayerEntityId != player.Entity.Id
            || Time.RealTime - pending.ReceivedTime > ServerControlTimeout) {
            return false;
        }

        if (!SetVehicleOrders(vehicle, pending.Request)) {
            s_pendingControls.Remove(vehicleEntity.Id);
            return false;
        }

        skipVanilla = true;
        return true;
    }

    public static void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (!NetworkManager.IsServerRunning
            || packet?.Payload == null
            || playerData?.ComponentPlayer == null) {
            return;
        }

        WonderfulEraVehicleControlRequest request;
        try {
            request = WonderfulEraCompatNetPayloads.ReadVehicleControlRequest(packet.Payload);
        }
        catch {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentMount mount = player.ComponentRider?.Mount;
        if (mount?.Entity == null
            || mount.Entity.Id != request.VehicleEntityId
            || !TryGetVehicleComponent(mount.Entity, out Component vehicle, out bool subVehicle)
            || subVehicle != request.SubVehicle) {
            return;
        }

        if (!SetVehicleOrders(vehicle, request)) {
            return;
        }

        s_pendingControls[request.VehicleEntityId] = new PendingControl {
            PlayerEntityId = player.Entity.Id,
            Request = request,
            ReceivedTime = Time.RealTime
        };
    }

    private static bool TryGetVehicleComponent(
        Entity entity,
        out Component vehicle,
        out bool subVehicle) {
        vehicle = null;
        subVehicle = false;
        if (entity == null) {
            return false;
        }

        vehicle = entity.Components.FirstOrDefault(component => IsType(component, VehicleTypeName));
        if (vehicle != null) {
            return true;
        }

        vehicle = entity.Components.FirstOrDefault(component => IsType(component, SubVehicleTypeName));
        subVehicle = vehicle != null;
        return vehicle != null;
    }

    private static bool SetVehicleOrders(
        Component vehicle,
        WonderfulEraVehicleControlRequest request) {
        if (vehicle == null
            || request == null
            || !float.IsFinite(request.MoveOrder)
            || !float.IsFinite(request.TurnOrder)
            || !float.IsFinite(request.FlyOrderY)) {
            return false;
        }

        if (!SetProperty(vehicle, "MoveOrder", request.MoveOrder)
            || !SetProperty(vehicle, "TurnOrder", request.TurnOrder)) {
            return false;
        }

        return SetProperty(
            vehicle,
            "FlyOrder",
            new Vector3(0f, request.FlyOrderY, 0f));
    }

    private static bool SetProperty(object instance, string name, object value) {
        PropertyInfo property = FindProperty(instance?.GetType(), name);
        if (property?.CanWrite != true) {
            return false;
        }

        try {
            property.SetValue(instance, value);
            return true;
        }
        catch {
            return false;
        }
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

    private static bool IsType(Component component, string typeName) {
        return component?.GetType().FullName == typeName
            && IsTargetAssembly(component.GetType().Assembly);
    }

    private static bool IsTargetAssembly(Assembly assembly) {
        if (assembly == null) {
            return false;
        }
        return NamesEqual(assembly.GetName().Name, TargetAssemblyName)
            || NamesEqual(assembly.ManifestModule?.Name, TargetAssemblyName);
    }

    private static bool NamesEqual(string left, string right) {
        return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        string result = value.Replace('\\', '/');
        int separator = result.LastIndexOf('/');
        if (separator >= 0) {
            result = result[(separator + 1)..];
        }
        return result.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? result[..^4]
            : result;
    }
}
