namespace Game.Network;

internal readonly record struct NetworkDigServerRequest(
    PlayerDigRequestPacket Packet,
    int PlayerIndex,
    int CreatedFrame);

internal sealed class NetworkDigServerRequestWindow {
    private readonly int m_maximumTotal;
    private readonly int m_maximumPerPlayer;
    private readonly Dictionary<RequestKey, NetworkDigServerRequest> m_requests = [];

    private readonly record struct RequestKey(int PlayerIndex, int ActionId);

    public NetworkDigServerRequestWindow(int maximumTotal, int maximumPerPlayer) {
        if (maximumTotal <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumTotal));
        }
        if (maximumPerPlayer <= 0 || maximumPerPlayer > maximumTotal) {
            throw new ArgumentOutOfRangeException(nameof(maximumPerPlayer));
        }
        m_maximumTotal = maximumTotal;
        m_maximumPerPlayer = maximumPerPlayer;
    }

    public int Count => m_requests.Count;

    public int CountForPlayer(int playerIndex) =>
        m_requests.Keys.Count(key => key.PlayerIndex == playerIndex);

    public bool Contains(int playerIndex, int actionId) =>
        m_requests.ContainsKey(new RequestKey(playerIndex, actionId));

    public bool TryAdd(
        PlayerDigRequestPacket packet,
        int playerIndex,
        int createdFrame
    ) {
        if (packet == null || playerIndex < 0 || packet.ActionId <= 0) {
            return false;
        }
        RequestKey key = new(playerIndex, packet.ActionId);
        if (m_requests.ContainsKey(key)
            || m_requests.Count >= m_maximumTotal
            || CountForPlayer(playerIndex) >= m_maximumPerPlayer) {
            return false;
        }
        m_requests.Add(key, new NetworkDigServerRequest(
            packet,
            playerIndex,
            createdFrame));
        return true;
    }

    public bool TryRemove(
        int playerIndex,
        int actionId,
        out NetworkDigServerRequest request
    ) => m_requests.Remove(new RequestKey(playerIndex, actionId), out request);

    public NetworkDigServerRequest[] TakeMatching(int playerIndex, CellFace cellFace) =>
        TakeWhere(request => request.PlayerIndex == playerIndex
            && SameCell(request.Packet.CellFace, cellFace));

    public NetworkDigServerRequest[] TakeExpired(int currentFrame, int timeoutFrames) {
        if (timeoutFrames < 0) {
            throw new ArgumentOutOfRangeException(nameof(timeoutFrames));
        }
        return TakeWhere(request => currentFrame - request.CreatedFrame >= timeoutFrames);
    }

    public void ClearPlayer(int playerIndex) {
        foreach (RequestKey key in m_requests.Keys
                     .Where(key => key.PlayerIndex == playerIndex)
                     .ToArray()) {
            m_requests.Remove(key);
        }
    }

    public void Clear() => m_requests.Clear();

    private NetworkDigServerRequest[] TakeWhere(
        Func<NetworkDigServerRequest, bool> predicate
    ) {
        NetworkDigServerRequest[] matches = m_requests.Values
            .Where(predicate)
            .OrderBy(request => request.CreatedFrame)
            .ThenBy(request => request.PlayerIndex)
            .ThenBy(request => request.Packet.ActionId)
            .ToArray();
        foreach (NetworkDigServerRequest request in matches) {
            m_requests.Remove(new RequestKey(
                request.PlayerIndex,
                request.Packet.ActionId));
        }
        return matches;
    }

    private static bool SameCell(CellFace first, CellFace second) =>
        first.X == second.X && first.Y == second.Y && first.Z == second.Z;
}
