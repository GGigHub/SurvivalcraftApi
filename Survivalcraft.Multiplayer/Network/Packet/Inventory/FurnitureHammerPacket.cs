using Engine;

namespace Game.Network;

public sealed class FurnitureHammerPacket : Packet {
    private enum EventType : byte {
        Request,
        Design,
        Reject
    }

    private static readonly Dictionary<int, double> s_lastRequestTimes = [];

    private EventType m_eventType;
    private byte[] m_designData = [];
    private CellFace m_start;
    private int m_designIndex = -1;
    private string m_message = string.Empty;

    public override byte ID => (byte)PacketType.FurnitureHammer;

    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public override byte Channel => NetworkChannels.Terrain;

    public FurnitureHammerPacket() { }

    public FurnitureHammerPacket(FurnitureDesign design, CellFace start) {
        m_eventType = EventType.Request;
        m_start = start;
        m_designData = design.Save().ToMessagePack();
    }

    private FurnitureHammerPacket(FurnitureDesign design) {
        m_eventType = EventType.Design;
        m_designIndex = design.Index;
        m_designData = design.Save().ToMessagePack();
    }

    private FurnitureHammerPacket(string message) {
        m_eventType = EventType.Reject;
        m_message = message ?? string.Empty;
    }

    public static void ClearState() {
        s_lastRequestTimes.Clear();
    }

    public static void ClearPlayerState(int playerIndex) {
        if (playerIndex >= 0) {
            s_lastRequestTimes.Remove(playerIndex);
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_eventType);
        switch (m_eventType) {
            case EventType.Request:
                archive.Buffer(ref m_designData, SubsystemFurnitureBlockBehavior.MaxNetworkFurnitureDesignDataLength);
                archive.Value(ref m_start);
                break;
            case EventType.Design:
                archive.Value(ref m_designIndex);
                archive.Buffer(ref m_designData, SubsystemFurnitureBlockBehavior.MaxNetworkFurnitureDesignDataLength);
                break;
            case EventType.Reject:
                archive.Value(ref m_message);
                break;
            default:
                throw new InvalidDataException("Unknown furniture hammer event type");
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            HandleRequest();
        }
        else {
            HandleClientEvent();
        }
    }

    private void HandleRequest() {
        if (m_eventType != EventType.Request || From == null || !AcquireRequestSlot(From.PlayerIndex)) {
            return;
        }
        PlayerData playerData = From.GetPlayerData();
        ComponentPlayer player = playerData?.ComponentPlayer;
        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        SubsystemFurnitureBlockBehavior furniture = GameManager.Project?.FindSubsystem<SubsystemFurnitureBlockBehavior>();
        if (player == null || terrain == null || furniture == null) {
            Reject("家具锤请求无法处理");
            return;
        }
        if (!SubsystemFurnitureBlockBehavior.TryDeserializeNetworkDesign(m_designData, terrain, out FurnitureDesign requestedDesign, out string error)
            || !furniture.TryPrepareNetworkFurnitureHammer(
                player,
                requestedDesign,
                m_start,
                out FurnitureDesign design,
                out Dictionary<Point3, int> valuesDictionary,
                out int startValue,
                out error
            )) {
            Reject(string.IsNullOrEmpty(error) ? "家具锤请求无效" : error);
            return;
        }

        // The design packet and the subsequent terrain packets share the Terrain channel,
        // so clients install the design before processing the authoritative cell changes.
        NetworkManager.Queue(new FurnitureHammerPacket(design));
        furniture.CommitFurnitureHammer(
            player.ComponentMiner,
            design,
            valuesDictionary,
            m_start,
            startValue,
            Terrain.ExtractContents(startValue) == 227
        );
    }

    private void HandleClientEvent() {
        SubsystemFurnitureBlockBehavior furniture = GameManager.Project?.FindSubsystem<SubsystemFurnitureBlockBehavior>();
        if (furniture == null) {
            return;
        }
        if (m_eventType == EventType.Design) {
            if (m_designIndex < 0 || m_designIndex >= FurnitureDesign.maxDesign) {
                return;
            }
            SubsystemTerrain terrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            if (SubsystemFurnitureBlockBehavior.TryDeserializeNetworkDesign(m_designData, terrain, out FurnitureDesign design, out _)) {
                furniture.AddDesign(m_designIndex, design);
            }
        }
        else if (m_eventType == EventType.Reject && !string.IsNullOrEmpty(m_message)) {
            GameManager.Project.FindSubsystem<SubsystemPlayers>()?.MainPlayer?.ComponentGui.DisplaySmallMessage(
                m_message,
                Color.White,
                true,
                false
            );
        }
    }

    private void Reject(string message) {
        if (From != null) {
            NetworkManager.Queue(new FurnitureHammerPacket(message) { To = From });
        }
    }

    private static bool AcquireRequestSlot(int playerIndex) {
        if (playerIndex < 0) {
            return false;
        }
        double now = Time.RealTime;
        if (s_lastRequestTimes.TryGetValue(playerIndex, out double last)
            && now - last < 0.25) {
            return false;
        }
        s_lastRequestTimes[playerIndex] = now;
        return true;
    }
}
