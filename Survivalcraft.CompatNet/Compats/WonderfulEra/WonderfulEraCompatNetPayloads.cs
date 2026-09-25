using Engine;
using Game.Network;

namespace Game;

public enum WonderfulEraCompatNetOperation : ushort {
    VehicleControlRequest = 1,
    ProjectileRequest = 2,
    RiderDismountRequest = 3
}

public sealed class WonderfulEraProjectileRequest {
    public int Value;
    public string ProjectileTypeName = string.Empty;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Value);
        if (archive.IsWriting && ProjectileTypeName == null) {
            ProjectileTypeName = string.Empty;
        }
        archive.Value(ref ProjectileTypeName);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
        archive.Value(ref AngularVelocity);
        if (Value <= 0
            || ProjectileTypeName.Length > WonderfulEraCompatNetPayloads.MaximumProjectileTypeNameLength
            || !IsFinite(Position)
            || !IsFinite(Velocity)
            || !IsFinite(AngularVelocity)
            || Velocity.LengthSquared() > WonderfulEraCompatNetPayloads.MaximumProjectileSpeedSquared) {
            throw new InvalidDataException("Invalid WonderfulEra projectile request.");
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}

public sealed class WonderfulEraVehicleControlRequest {
    public int VehicleEntityId;
    public bool SubVehicle;
    public float MoveOrder;
    public float TurnOrder;
    public float FlyOrderY;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref VehicleEntityId);
        archive.Value(ref SubVehicle);
        archive.Value(ref MoveOrder);
        archive.Value(ref TurnOrder);
        archive.Value(ref FlyOrderY);
        if (VehicleEntityId <= 0
            || !float.IsFinite(MoveOrder)
            || !float.IsFinite(TurnOrder)
            || !float.IsFinite(FlyOrderY)
            || Math.Abs(MoveOrder) > WonderfulEraCompatNetPayloads.MaximumMoveOrder
            || Math.Abs(TurnOrder) > WonderfulEraCompatNetPayloads.MaximumTurnOrder
            || Math.Abs(FlyOrderY) > WonderfulEraCompatNetPayloads.MaximumFlyOrderY) {
            throw new InvalidDataException("Invalid WonderfulEra vehicle control request.");
        }
    }
}

public static class WonderfulEraCompatNetPayloads {
    public const float MaximumMoveOrder = 1f;
    public const float MaximumTurnOrder = 1f;
    public const float MaximumFlyOrderY = 10f;
    public const float MaximumProjectileSpeedSquared = 10000f;
    public const int MaximumProjectileTypeNameLength = 256;

    public static byte[] WriteVehicleControlRequest(
        WonderfulEraVehicleControlRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static WonderfulEraVehicleControlRequest ReadVehicleControlRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        WonderfulEraVehicleControlRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteProjectileRequest(WonderfulEraProjectileRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static WonderfulEraProjectileRequest ReadProjectileRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        WonderfulEraProjectileRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }
}
