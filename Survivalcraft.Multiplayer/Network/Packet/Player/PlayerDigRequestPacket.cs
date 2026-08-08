namespace Game.Network;

public enum PlayerDigRequestFailureReason : byte {
    None,
    ServerDidNotObserve,
    ValueMismatch,
    InvalidRequest
}

/// <summary>
/// Sent by a client when a locally completed dig has not been confirmed by a
/// server terrain update. The server validates the absolute target coordinate
/// and may complete the dig directly within its recovery bounds.
/// </summary>
public sealed class PlayerDigRequestPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDigRequest;
    public override byte Channel => NetworkChannels.Terrain;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public int ActionId;
    public CellFace CellFace;
    public int OldValue;
    public int ExpectedValue;

    public PlayerDigRequestPacket() { }

    public PlayerDigRequestPacket(
        int actionId,
        ComponentPlayer player,
        CellFace cellFace,
        int oldValue,
        int expectedValue
    ) {
        ActionId = actionId;
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        CellFace = cellFace;
        OldValue = oldValue;
        ExpectedValue = expectedValue;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, null);
        archive.Value(ref ActionId);
        archive.Value(ref CellFace);
        archive.Value(ref OldValue);
        archive.Value(ref ExpectedValue);
    }

    public override void Handle(bool isServer) {
        if (!isServer || !TryResolvePlayer(true, out PlayerData playerData)) {
            return;
        }

        NetworkDigConfirmationRuntime.ReceiveServerRequest(this, playerData);
    }
}
