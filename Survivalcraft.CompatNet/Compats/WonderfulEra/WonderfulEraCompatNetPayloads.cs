using Game.Network;

namespace Game;

public enum WonderfulEraCompatNetOperation : ushort {
    VehicleControlRequest = 1
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
}
