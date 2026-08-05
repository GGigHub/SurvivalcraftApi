namespace Game.Network;

/// <summary>
/// Server response for a delayed client dig request.
/// </summary>
public sealed class PlayerDigConfirmationPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDigConfirmation;
    public override byte Channel => NetworkChannels.Terrain;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public int ActionId;
    public CellFace CellFace;
    public int ExpectedValue;
    public int ActualValue;
    public bool Success;
    public PlayerDigRequestFailureReason FailureReason;

    public PlayerDigConfirmationPacket() { }

    public PlayerDigConfirmationPacket(
        PlayerDigRequestPacket request,
        int actualValue,
        bool success,
        PlayerDigRequestFailureReason failureReason
    ) {
        ActionId = request.ActionId;
        m_fromPlayerIndex = request.m_fromPlayerIndex;
        CellFace = request.CellFace;
        ExpectedValue = request.ExpectedValue;
        ActualValue = actualValue;
        Success = success;
        FailureReason = failureReason;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref ActionId);
        archive.Value(ref CellFace);
        archive.Value(ref ExpectedValue);
        archive.Value(ref ActualValue);
        archive.Value(ref Success);
        archive.Enum(ref FailureReason);
    }

    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out _)) {
            return;
        }

        NetworkDigConfirmationRuntime.ReceiveClientConfirmation(this);
    }
}
