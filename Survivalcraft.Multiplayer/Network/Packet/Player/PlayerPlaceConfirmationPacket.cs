namespace Game.Network;

/// <summary>
/// Server response for a client-side predicted block placement.
/// </summary>
public sealed class PlayerPlaceConfirmationPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerPlaceConfirmation;
    public override byte Channel => NetworkChannels.Terrain;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public int ActionId;
    public CellFace CellFace;
    public int ExpectedValue;
    public int ActualValue;
    public bool Success;

    public PlayerPlaceConfirmationPacket() { }

    public PlayerPlaceConfirmationPacket(
        ComponentPlayer player,
        ComponentPlayer.InteractEventItem request,
        int actualValue,
        bool success
    ) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        ActionId = request.PlaceActionId;
        CellFace = request.PlaceCellFace;
        ExpectedValue = request.PlaceExpectedValue;
        ActualValue = actualValue;
        Success = success;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref ActionId);
        archive.Value(ref CellFace);
        archive.Value(ref ExpectedValue);
        archive.Value(ref ActualValue);
        archive.Value(ref Success);
    }

    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out _)) {
            return;
        }

        NetworkPlaceConfirmationRuntime.ReceiveClientConfirmation(this);
    }
}
