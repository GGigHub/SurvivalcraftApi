using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerInteractPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerInteract;
    public ComponentPlayer.InteractEvent m_interactEvent;
    public ComponentPlayer.InteractAction m_action;
    public Ray3 m_netInteractRay;
    public TerrainRaycastResult? m_netInteractRaycast;
    public bool m_hasPlacePrediction;
    public int m_placeActionId;
    public CellFace m_placeCellFace;
    public int m_placeOldValue;
    public int m_placeExpectedValue;

    public PlayerInteractPacket() { }
    public PlayerInteractPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_interactEvent = player.interactEvent.interactEvent;
        m_action = player.interactEvent.action;
        m_netInteractRay = player.interactEvent.NetInteractRay;
        m_netInteractRaycast = player.interactEvent.NetPlaceRaycast;
        if (m_action == ComponentPlayer.InteractAction.Place
            && NetworkPlaceConfirmationRuntime.TryRegisterClientPrediction(
                player,
                out m_placeActionId,
                out ComponentMiner.PlacementOperation operation
            )) {
            m_hasPlacePrediction = true;
            m_placeCellFace = operation.CellFace;
            m_placeOldValue = operation.OldValue;
            m_placeExpectedValue = operation.Value;
        }
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Enum(ref m_interactEvent);
        archive.Enum(ref m_action);
        archive.Value(ref m_netInteractRay);
        archive.Value(ref m_netInteractRaycast);
        archive.Value(ref m_hasPlacePrediction);
        archive.Value(ref m_placeActionId);
        archive.Value(ref m_placeCellFace);
        archive.Value(ref m_placeOldValue);
        archive.Value(ref m_placeExpectedValue);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddInteractEvent(
            m_interactEvent,
            m_action,
            m_netInteractRay,
            m_netInteractRaycast,
            m_hasPlacePrediction,
            m_placeActionId,
            m_placeCellFace,
            m_placeOldValue,
            m_placeExpectedValue
        );
        QueuePlayerPacket(this, isServer);
    }
}


