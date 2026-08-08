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
    private long m_requestId;
    private FurnitureHammerResultCode m_resultCode;
    private byte[] m_designData = [];
    private CellFace m_start;
    private int m_designIndex = -1;
    private int m_producedValue;
    private int m_producedCount;
    private string m_message = string.Empty;

    public override byte ID => (byte)PacketType.FurnitureHammer;

    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public override byte Channel => NetworkChannels.Terrain;

    public long RequestId => m_requestId;

    public FurnitureHammerPacket() { }

    public FurnitureHammerPacket(FurnitureDesign design, CellFace start, long requestId = 0) {
        ArgumentNullException.ThrowIfNull(design);
        m_eventType = EventType.Request;
        m_requestId = requestId > 0
            ? requestId
            : NetworkMutationRequestTracker.CreateRequestId();
        m_start = start;
        m_designData = design.Save().ToMessagePack();
        FurnitureHammerResultTracker.TryReserve(m_requestId);
    }

    private FurnitureHammerPacket(
        FurnitureDesign design,
        long requestId,
        int producedValue,
        int producedCount) {
        m_eventType = EventType.Design;
        m_requestId = requestId;
        m_resultCode = FurnitureHammerResultCode.Accepted;
        m_designIndex = design.Index;
        m_producedValue = producedValue;
        m_producedCount = producedCount;
        m_designData = design.Save().ToMessagePack();
    }

    private FurnitureHammerPacket(
        long requestId,
        FurnitureHammerResultCode resultCode,
        string message) {
        m_eventType = EventType.Reject;
        m_requestId = requestId;
        m_resultCode = resultCode;
        m_message = message ?? string.Empty;
    }

    public static void ClearState() {
        s_lastRequestTimes.Clear();
        FurnitureHammerResultTracker.Reset();
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
                archive.Value(ref m_requestId);
                archive.Buffer(
                    ref m_designData,
                    SubsystemFurnitureBlockBehavior.MaxNetworkFurnitureDesignDataLength);
                archive.Value(ref m_start);
                break;
            case EventType.Design:
                archive.Value(ref m_requestId);
                archive.Enum(ref m_resultCode);
                archive.Value(ref m_designIndex);
                archive.Value(ref m_producedValue);
                archive.Value(ref m_producedCount);
                archive.Buffer(
                    ref m_designData,
                    SubsystemFurnitureBlockBehavior.MaxNetworkFurnitureDesignDataLength);
                break;
            case EventType.Reject:
                archive.Value(ref m_requestId);
                archive.Enum(ref m_resultCode);
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
        if (m_eventType != EventType.Request || From == null) {
            return;
        }
        if (m_requestId <= 0) {
            Reject(FurnitureHammerResultCode.InvalidRequest, "家具锤请求无效");
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.FurnitureHammer,
                m_requestId)) {
            return;
        }
        if (!AcquireRequestSlot(From.PlayerIndex)) {
            Reject(FurnitureHammerResultCode.RateLimited, "家具锤请求过于频繁");
            return;
        }
        PlayerData playerData = From.GetPlayerData();
        ComponentPlayer player = playerData?.ComponentPlayer;
        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        SubsystemFurnitureBlockBehavior furniture =
            GameManager.Project?.FindSubsystem<SubsystemFurnitureBlockBehavior>();
        if (player == null || terrain == null || furniture == null) {
            Reject(FurnitureHammerResultCode.InvalidRequest, "家具锤请求无法处理");
            return;
        }
        if (!SubsystemFurnitureBlockBehavior.TryDeserializeNetworkDesign(
                m_designData,
                terrain,
                out FurnitureDesign requestedDesign,
                out string error)) {
            Reject(
                FurnitureHammerResultCode.InvalidDesign,
                string.IsNullOrEmpty(error) ? "家具设计数据无效" : error);
            return;
        }
        if (!furniture.TryPrepareNetworkFurnitureHammer(
                player,
                requestedDesign,
                m_start,
                out FurnitureDesign design,
                out Dictionary<Point3, int> valuesDictionary,
                out int startValue,
                out FurnitureHammerResultCode resultCode,
                out error)) {
            Reject(
                resultCode == FurnitureHammerResultCode.None
                    ? FurnitureHammerResultCode.InvalidRequest
                    : resultCode,
                string.IsNullOrEmpty(error) ? "家具锤请求无效" : error);
            return;
        }

        // Design and subsequent terrain/pickable packets share the Terrain
        // channel. Queue the accepted design after final commit validation but
        // before mutation, so clients always install its index first.
        bool committed = furniture.CommitFurnitureHammer(
            player.ComponentMiner,
            design,
            valuesDictionary,
            m_start,
            startValue,
            Terrain.ExtractContents(startValue) == FurnitureBlock.Index,
            (producedValue, producedCount) => NetworkManager.Queue(
                new FurnitureHammerPacket(
                    design,
                    m_requestId,
                    producedValue,
                    producedCount)));
        if (!committed) {
            Reject(FurnitureHammerResultCode.CommitFailed, "家具锤提交失败");
        }
    }

    private void HandleClientEvent() {
        if (m_eventType == EventType.Reject) {
            if (m_requestId > 0
                && Enum.IsDefined(m_resultCode)
                && m_resultCode != FurnitureHammerResultCode.None
                && m_resultCode != FurnitureHammerResultCode.Accepted) {
                FurnitureHammerResultTracker.Complete(new NetworkFurnitureHammerResult(
                    m_requestId,
                    m_resultCode,
                    -1,
                    0,
                    0,
                    m_message));
            }
            if (!string.IsNullOrEmpty(m_message)) {
                GameManager.Project?.FindSubsystem<SubsystemPlayers>()
                    ?.MainPlayer?.ComponentGui.DisplaySmallMessage(
                        m_message,
                        Color.White,
                        true,
                        false);
            }
            return;
        }
        if (m_eventType != EventType.Design
            || m_requestId <= 0
            || m_resultCode != FurnitureHammerResultCode.Accepted
            || m_designIndex < 0
            || m_designIndex >= FurnitureDesign.maxDesign
            || m_producedValue == 0
            || m_producedCount <= 0) {
            return;
        }
        SubsystemFurnitureBlockBehavior furniture =
            GameManager.Project?.FindSubsystem<SubsystemFurnitureBlockBehavior>();
        SubsystemTerrain terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>();
        string error = string.Empty;
        if (furniture == null
            || terrain == null
            || !SubsystemFurnitureBlockBehavior.TryDeserializeNetworkDesign(
                m_designData,
                terrain,
                out FurnitureDesign design,
                out error)) {
            FurnitureHammerResultTracker.Complete(new NetworkFurnitureHammerResult(
                m_requestId,
                FurnitureHammerResultCode.ClientStateUnavailable,
                m_designIndex,
                m_producedValue,
                m_producedCount,
                string.IsNullOrEmpty(error) ? "客户端家具设计状态不可用" : error));
            return;
        }
        furniture.AddDesign(m_designIndex, design);
        FurnitureHammerResultTracker.Complete(new NetworkFurnitureHammerResult(
            m_requestId,
            FurnitureHammerResultCode.Accepted,
            m_designIndex,
            m_producedValue,
            m_producedCount,
            string.Empty));
    }

    private void Reject(FurnitureHammerResultCode resultCode, string message) {
        if (From != null) {
            NetworkManager.Queue(new FurnitureHammerPacket(
                m_requestId,
                resultCode,
                message) { To = From });
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
