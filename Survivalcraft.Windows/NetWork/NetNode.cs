using LiteNetLib;
using Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using LiteNetLib.Utils;
using Game.NetWork.Packages;
using GameEntitySystem;

namespace Game.NetWork;

public class NetNode
{
    public enum Stage
    {
        NotConnected,
        WaitForClientList,
        Connected
    }

    public readonly EventBasedNetListener listener;

    public readonly NetManager netManager;

    public Client Self { get; set; }

    public Client Server { get; set; }

    public string Error { get; private set; }

    public Stage CurrentStage { get; set; } = Stage.NotConnected;

    public bool IsConnected => CurrentStage == Stage.Connected;

    public bool IsServer => CurrentStage == Stage.Connected && Self == Server;

    private long m_lastConnectTime = 0;

    private const byte verifyByte = 0x88;
    private const uint chunkMagic = 0x4E46434B; // "NFCK"
    private const byte chunkVersion = 1;
    private const int minChunkSize = 256;
    private const int maxChunkSize = 60 * 1024;
    private const int chunkHeaderBytes = 19;
    private const double chunkStateTimeoutSeconds = 15.0;
    private static int s_chunkMessageId = 1;
    private readonly Dictionary<string, ChunkState> m_chunkStates = new Dictionary<string, ChunkState>();
    private double m_lastChunkCleanupTime;

    public int ClientCount
    {
        get
        {

            return Peers.Count() + 1;
        }
    }

    public Dictionary<int, Client> Clients = new Dictionary<int, Client>();

    public NetPeer pendingPeer = null;
    // 鍙湁杩欎簺瀹㈡埛绔悓鎰忎互鍚庢柊瀹㈡埛绔墠鑳藉姞鍏?
    public HashSet<byte> agreeOnPendingPeer = new HashSet<byte>();

    private Client pendingClient => (Client)pendingPeer?.Tag;

    public IEnumerable<Client> Peers => Clients.Values.Where(c => c != Self);

    // 鎺у埗鏂板鎴风鍔犲叆鐨勭瓑寰呯獥鍙ｏ紝闃叉鏃у鎴风 DeliveryEvent 涓㈠け瀵艰嚧涓€鐩村崱鍦?pending 鐘舵€?
    private DateTime? pendingStartUtc;
    private const double PendingJoinTimeoutSeconds = 2.0; // 瓒呮椂鑷姩瀹屾垚鍔犲叆锛屽崟浣嶇

    /// <summary>
    /// 鍖呭彂閫侀槦鍒?
    /// </summary>
    private List<IPackage> PendingPackages = new List<IPackage>();
    /// <summary>
    /// 鍖呭鐞嗛槦鍒?
    /// </summary>
    private List<IPackage> PendingHanldePackages = new List<IPackage>();

    // 褰撲竴涓鎴风鏀跺埌涓€涓秷鎭?
    public event Action<NetNode, IEnumerable<IPackage>> OnRecieve;
    public Action<Client> OnClientStateChanged;
    public Guid TokenId;
    private Dictionary<Client, string> ClientsToRemove = new Dictionary<Client, string>();
    private bool isStopping;
    private long sec_length;
    private long sec_length_total;
    private long sec_total;
    public bool AllowHanlde = true;
    public WorkType WorkType { get; set; }
    public NetNode()
    {
        listener = new EventBasedNetListener();
        netManager = new NetManager(listener)
        {
            MaxConnectAttempts = 6,
            DisconnectTimeout = CommonLib.DISCONNECT_TIMEOUT,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        var broadcastListener = new EventBasedNetListener();
        broadcastListener.NetworkReceiveUnconnectedEvent += HanldeBroadcastUnconnectedEvent;
    }

    private class ChunkState
    {
        public int TotalLength;
        public int ChunkSize;
        public int TotalChunks;
        public int ReceivedChunks;
        public DateTime LastUpdateUtc;
        public byte[] Buffer;
        public bool[] Received;
    }

    private static int GetConfiguredChunkSize()
    {
        int configured = SettingsManager.NetworkChunkSize;
        if (configured < minChunkSize) return minChunkSize;
        if (configured > maxChunkSize) return maxChunkSize;
        return configured;
    }

    private static NetDataWriter BuildPayloadWriter(byte[] payload)
    {
        NetDataWriter writer = new NetDataWriter();
        writer.PutBytesWithLength(payload);
        return writer;
    }

    private static NetDataWriter BuildChunkWriter(int messageId, int totalLength, int offset, byte[] payload, int chunkSize)
    {
        int length = Math.Min(chunkSize, totalLength - offset);
        byte[] chunk = new byte[length];
        Buffer.BlockCopy(payload, offset, chunk, 0, length);
        NetDataWriter writer = new NetDataWriter();
        writer.Put(chunkMagic);
        writer.Put(chunkVersion);
        writer.Put(messageId);
        writer.Put(totalLength);
        writer.Put(offset);
        writer.Put((ushort)length);
        writer.Put(chunk);
        return writer;
    }

    private static string BuildChunkStateKey(string sourceKey, int messageId)
    {
        return $"{sourceKey}:{messageId}";
    }

    private static string GetSourceKey(NetPeer peer = null, ConnectionRequest request = null, IPEndPoint endPoint = null)
    {
        if (peer != null) return "peer:" + peer.Address;
        if (request != null) return "req:" + request.RemoteEndPoint;
        if (endPoint != null) return "ep:" + endPoint;
        return "unknown";
    }

    private bool TryReadChunkFrame(NetDataReader reader, out int messageId, out int totalLength, out int offset, out byte[] chunkData)
    {
        messageId = 0;
        totalLength = 0;
        offset = 0;
        chunkData = null;
        if (reader == null || reader.AvailableBytes < chunkHeaderBytes)
        {
            return false;
        }

        var probe = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        if (probe.GetUInt() != chunkMagic)
        {
            return false;
        }

        byte version = probe.GetByte();
        if (version != chunkVersion)
        {
            return true;
        }

        messageId = probe.GetInt();
        totalLength = probe.GetInt();
        offset = probe.GetInt();
        int length = probe.GetUShort();
        if (length <= 0 || totalLength <= 0 || offset < 0 || offset + length > totalLength || probe.AvailableBytes < length)
        {
            return true;
        }

        chunkData = new byte[length];
        probe.GetBytes(chunkData, length);
        return true;
    }

    private bool TryAssembleChunk(string sourceKey, int messageId, int totalLength, int offset, byte[] chunkData, out byte[] payload)
    {
        payload = null;
        if (chunkData == null || chunkData.Length == 0)
        {
            return false;
        }

        int chunkSize = GetConfiguredChunkSize();
        int totalChunks = (totalLength + chunkSize - 1) / chunkSize;
        if (totalChunks <= 0)
        {
            return false;
        }

        int index = offset / chunkSize;
        if (index < 0 || index >= totalChunks)
        {
            return false;
        }

        string key = BuildChunkStateKey(sourceKey, messageId);
        if (!m_chunkStates.TryGetValue(key, out ChunkState state))
        {
            state = new ChunkState
            {
                TotalLength = totalLength,
                ChunkSize = chunkSize,
                TotalChunks = totalChunks,
                ReceivedChunks = 0,
                LastUpdateUtc = DateTime.UtcNow,
                Buffer = new byte[totalLength],
                Received = new bool[totalChunks]
            };
            m_chunkStates[key] = state;
        }
        else if (state.TotalLength != totalLength || state.ChunkSize != chunkSize || state.TotalChunks != totalChunks)
        {
            m_chunkStates.Remove(key);
            return false;
        }

        if (!state.Received[index])
        {
            Buffer.BlockCopy(chunkData, 0, state.Buffer, offset, chunkData.Length);
            state.Received[index] = true;
            state.ReceivedChunks++;
        }
        state.LastUpdateUtc = DateTime.UtcNow;

        if (state.ReceivedChunks >= state.TotalChunks)
        {
            payload = state.Buffer;
            m_chunkStates.Remove(key);
            return true;
        }

        return false;
    }

    private void CleanupChunkStates()
    {
        double now = Time.RealTime;
        if (now - m_lastChunkCleanupTime < 1.0)
        {
            return;
        }
        m_lastChunkCleanupTime = now;
        DateTime utcNow = DateTime.UtcNow;
        var keys = new List<string>();
        foreach (var pair in m_chunkStates)
        {
            if ((utcNow - pair.Value.LastUpdateUtc).TotalSeconds > chunkStateTimeoutSeconds)
            {
                keys.Add(pair.Key);
            }
        }
        foreach (var key in keys)
        {
            m_chunkStates.Remove(key);
        }
    }

    private void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false)
    {
        if (payload == null)
        {
            return;
        }

        int chunkSize = GetConfiguredChunkSize();
        bool connectPath = iPEndPoint != null && !useDeliveryEvent;
        if (payload.Length <= chunkSize || connectPath)
        {
            var writer = BuildPayloadWriter(payload);
            if (netPeer != null)
            {
                netPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else if (request != null)
            {
                netManager.SendUnconnectedMessage(writer, request.RemoteEndPoint);
            }
            else if (iPEndPoint != null)
            {
                if (!useDeliveryEvent) netManager.Connect(iPEndPoint, writer);
                else netManager.SendUnconnectedMessage(writer, iPEndPoint);
            }
            return;
        }

        int messageId = Interlocked.Increment(ref s_chunkMessageId);
        for (int offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var chunkWriter = BuildChunkWriter(messageId, payload.Length, offset, payload, chunkSize);
            if (netPeer != null)
            {
                netPeer.Send(chunkWriter, DeliveryMethod.ReliableOrdered);
            }
            else if (request != null)
            {
                netManager.SendUnconnectedMessage(chunkWriter, request.RemoteEndPoint);
            }
            else if (iPEndPoint != null)
            {
                netManager.SendUnconnectedMessage(chunkWriter, iPEndPoint);
            }
        }
    }

    //娣诲姞鍒伴槦鍒?
    public void QueuePackage(IPackage package)
    {
        lock (PendingPackages)
        {

            PendingPackages.Add(package);
        }
    }

    private byte FindUnusedIndex()
    {
        for (byte i = 0; i < 255; i++)
        {
            if (!Clients.ContainsKey(i)) return i;
        }
        throw new Exception("Server is full.");
    }


    // 鍒涘缓瀹㈡埛
    public Client CreateClient(NetPeer peer, Guid tokenId, Guid guid, string dataId, string nickname)
    {
        if (!IsServer) throw new InvalidOperationException("creating client as non-server");
        foreach (var p in Peers)
        {
            //涓嬩竴甯хЩ闄わ紝鍚﹀垯浼氭姤閿欓泦鍚堜互鏇存敼
            if (p.PlayerGuid == guid) RemoveClient(p, "Duplicate account login.");
        }
        var c = new Client(peer, FindUnusedIndex(), tokenId, guid, dataId, nickname);
        peer.Tag = c;
        return c;
    }

    public void AddClient(Client c)
    {
#if SERVER
        // 浼穿鏈嶇殑瑙﹀彂鍘熷洜鏄鎴风鍙戠幇Clients瀛楀吀涓湁鍜岃嚜宸辩浉鍚岀殑Guid鍜孋lientID
        // 浣嗘槸瀹㈡埛绔苟涓嶉渶瑕佽礋璐ｇ帺瀹剁殑寮傚父閫€鍑洪儴鍒?
        if (WorkType != WorkType.Server)
        {
            Clients[c.ID] = c;
        }
        else
        {
            var toRemove = new List<int>();
            foreach (var pair in Clients)
            {
                if (pair.Value.PlayerGuid == c.PlayerGuid)
                {
                    toRemove.Add(pair.Key);
                }
            }
            foreach (int k in toRemove)
            {
                RemoveClient(Clients[k], "Player logged in from another location.");
            }
            if (Clients.ContainsKey(c.ID))
            {
                RemoveClient(Clients[c.ID], "妫€娴嬪埌閲嶅鐨凜lientID锛岃閲嶆柊杩炴帴");
            }
            else
            {
                Clients[c.ID] = c;
            }
        }
#else
        Clients[c.ID] = c;
#endif
    }
    public void RemoveClient(Client c, string reason = null)
    {
        if (ClientsToRemove.ContainsKey(c))
        {
            if (string.IsNullOrEmpty(reason))
                ClientsToRemove[c] += '\n' + reason;
        }
        else
        {
            ClientsToRemove[c] = reason ?? string.Empty;
        }
    }

    // 鍒犻櫎瀹㈡埛
    public void RemoveClientImmediate(Client c, string reason = null)
    {
        if (c == Self)
        {
            // 濡傛灉鏄嚜宸憋紝鍋滄鏈嶅姟鍣?
            Stop();
            return;
        }

        if (c.Peer != null && c.Peer.ConnectionState != ConnectionState.Disconnected)
        {
            if (string.IsNullOrEmpty(reason)) c.Peer.Disconnect();
            else c.Peer.Disconnect(NetDataWriter.FromString(reason));
        }

        if (c == Server)
        {
            Stop();
            CurrentStage = Stage.NotConnected;
            return;
        }
        if (Clients.ContainsKey(c.ID))
        {
            if (IsServer)
            {
                QueuePackage(new ClientPackage(c.ID));
            }
            c.State = ClientState.NotConnected;
            OnClientStateChanged?.Invoke(c);
            Clients.Remove(c.ID);
        }
    }
    /// <summary>
    /// 鏈嶅姟鍣ㄥ叧闂紝涓嶆墽琛孫nClientStateChanged锛岄槻姝㈠娆ispose
    /// </summary>
    /// <param name="reason"></param>
    public void RemoveAllClients(string reason = null)
    {
        var cs = new List<Client>(Clients.Values);
        foreach (var c in cs)
        {
            if (c == Self) continue;
            if (c.Peer != null && c.Peer.ConnectionState != ConnectionState.Disconnected)
            {
                if (reason != null) c.Peer.Disconnect(NetDataWriter.FromString(reason));
                else c.Peer.Disconnect();
            }
            Clients.Remove(c.ID);
            c.State = ClientState.NotConnected;
        }
    }

    public Client GetClientByID(byte id)
    {
        if (Clients.ContainsKey(id))
            return Clients[id];
        return null;
    }

    public Client GetClientByGUID(Guid? guid)
    {
        return Clients.Values.FirstOrDefault(c => c.PlayerGuid == guid);
    }

    public Client GetClientByTokenID(Guid? guid)
    {
        return Clients.Values.FirstOrDefault(c => c.TokenId == guid);
    }

    public void HanldeUnconnectedEvent(IPEndPoint ip, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        UnconnectedReceiveEvent(ip, reader, false);
    }

    public void HanldeBroadcastUnconnectedEvent(IPEndPoint ip, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        UnconnectedReceiveEvent(ip, reader, true);
    }

    /// <summary>
    /// 杩炴帴鏂紑浜嬩欢 鏈嶅姟绔?
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="disconnectInfo"></param>
    private void PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log.Information($"{peer.Address}杩炴帴鍏抽棴: " + disconnectInfo.Reason);
        if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.TryGetString(out string str))
        {
            Log.Information("----Additional message----");
            Log.Information(str);
            Log.Information("----End of message----");
        }
        else str = disconnectInfo.Reason.ToString();

        if (WorkType == WorkType.Client)
        {
            Stop(str);
        }
        else
        {
            Client client = peer.Tag as Client;
            if (client != null)
            {
                client.State = ClientState.NotConnected;
                RemoveClientImmediate(client);
                OnClientStateChanged?.Invoke(client);
                //鍚屾Client鍒楄〃
                if (peer == pendingPeer)
                {
                    pendingPeer = null;
                    agreeOnPendingPeer.Clear();
                    pendingStartUtc = null;
                }
                else
                {
                    agreeOnPendingPeer.Remove(client.ID);
                    DeliveryEvent(null, null);
                }
            }
        }
    }
    /// <summary>
    /// 杩炴帴鏂紑浜嬩欢 瀹㈡埛绔?
    /// </summary>
    /// <param name="remoteEndPoint"></param>
    /// <param name="reader"></param>
    /// <param name="messageType"></param>
    private void UnconnectedReceiveEvent(IPEndPoint remoteEndPoint, NetPacketReader reader, bool fromBroadcast)
    {
        if (CurrentStage == Stage.NotConnected) return;
        var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        List<IPackage> list;
        if (TryReadChunkFrame(dataReader, out int messageId, out int totalLength, out int offset, out byte[] chunkData))
        {
            if (chunkData == null)
            {
                reader.Recycle();
                return;
            }
            string sourceKey = GetSourceKey(null, null, remoteEndPoint);
            if (!TryAssembleChunk(sourceKey, messageId, totalLength, offset, chunkData, out byte[] payload))
            {
                reader.Recycle();
                return;
            }
            list = PackageManager.DecodePackagesFromPayload(this, payload, null, null, remoteEndPoint);
        }
        else
        {
            list = PackageManager.DecodePackages(this, dataReader, null, null, remoteEndPoint);
        }
        try
        {
            foreach (var c in list)
            {
                c.From.IsLocalRemote = fromBroadcast;
                try
                {
                    c.Handle(null, this, IsServer);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("[{0}]{1}", c.GetType().Name, e.Message));

                }
            }
        }
        catch
        {
        }
        finally
        {
            reader.Recycle();
        }
    }
    public void AddPendingHanldePackage(IPackage package)
    {
#if DEBUG
        Log.Information("[鎺掗槦]娣诲姞鍒板鐞嗛槦鍒楋細" + package.ID);
#endif
        PendingHanldePackages.Add(package);
    }
    /// <summary>
    /// 鏁版嵁鎺ユ敹浜嬩欢
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="reader"></param>
    /// <param name="deliveryMethod"></param>
    public void NetworkReceiveEvent(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod)
    {
        if (CurrentStage == Stage.NotConnected) return;
        var fromClient = peer.Tag as Client;
        List<IPackage> list;
        if (TryReadChunkFrame(reader, out int messageId, out int totalLength, out int offset, out byte[] chunkData))
        {
            if (chunkData == null)
            {
                return;
            }
            string sourceKey = GetSourceKey(peer, null, null);
            if (!TryAssembleChunk(sourceKey, messageId, totalLength, offset, chunkData, out byte[] payload))
            {
                return;
            }
            list = PackageManager.DecodePackagesFromPayload(this, payload, peer);
        }
        else
        {
            list = PackageManager.DecodePackages(this, reader, peer);
        }
        list.InsertRange(0, PendingHanldePackages);
        PendingHanldePackages.Clear();
        try
        {
            if (OnRecieve == null)
            {
#if DEBUG
                Log.Information("妫€娴嬪埌鏈夋湭澶勭悊鐨勫寘:" + list.Count);
#endif
                PendingHanldePackages.AddRange(list);
            }
            else
            {
                OnRecieve?.Invoke(this, list);
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            RemoveClient(fromClient, "Error On RecieveEvent:\n" + e.ToString());
        }
        finally
        {
            // 娉ㄦ剰锛歂etDataReader涓嶉渶瑕丷ecycle()鏂规硶
        }
    }
    /// <summary>
    /// 杩炴帴琚嫆缁濅簨浠?
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="reader"></param>
    /// <param name="type"></param>
    public void HandleConnectionReject(IPEndPoint sender, NetDataReader reader, UnconnectedMessageType type)
    {
        try
        {
            ConnectionRejectPackage rejectPackage = PackageManager.DecodePackage<ConnectionRejectPackage>(this, reader, null, null, sender);
            rejectPackage.Handle(null, this, IsServer);
        }
        catch
        {
        }
    }
    /// <summary>
    /// 鏂板鎴风杩炴帴浜嬩欢
    /// </summary>
    /// <param name="request"></param>
    private void ConnectionRequestEvent(ConnectionRequest request)
    {
        //long now = CommonLib.GetMicroSeconds();
        //if (now - m_lastConnectTime < 10000)
        //{
        //    SendWriterFromPackage(new ConnectionRejectPackage("褰撳墠杩炴帴杈冨锛岃绛夊緟1绉掑姞鍏?), request, true);
        //    return;
        //}
        //else
        //{
        //    m_lastConnectTime = CommonLib.GetMicroSeconds();
        //}
        Log.Information("鎺ユ敹鍒拌繛鎺ヨ姹?" + request.RemoteEndPoint);



        if (ProjectNet.Project == null)
        {
            SendWriterFromPackage(new ConnectionRejectPackage("Please wait for server startup to complete."), request, true);
        }
        else if (pendingClient != null)
        {
            Log.Information($"Too Many Request And Reject Current Request:{request.RemoteEndPoint}");
            Log.Information($"Current Pending Client:{pendingClient}");
            SendWriterFromPackage(new ConnectionRejectPackage($"褰撳墠鏈嶅姟鍣ㄨ繛鎺ヨ姹傚お澶氾紝璇风◢鍊欏啀璇曪紝绛夊緟瀹㈡埛绔洖搴旀暟{agreeOnPendingPeer.Count}"), request, true);
        }
        else
        {
            try
            {
                ConnectionRequestPackage requestPackage = PackageManager.DecodePackage<ConnectionRequestPackage>(this, request.Data, null, request);
                requestPackage.Handle(ProjectNet.Project, this, true);
            }
            catch (Exception e)
            {
                SendWriterFromPackage(new ConnectionRejectPackage($"閿欒鐨勮繛鎺ヨ姹傛暟鎹寘:{e.Message}"), request, true);
                Log.Information("ConnectionRequestEvent Error:" + e.StackTrace);
            }
        }

    }
    public void StartLocal()
    {
        Clients.Clear();
        Self = new Client(new Guid(SettingsManager.OnlineAccessToken));
        AddClient(Self);
    }

    /// <summary>
    /// 寮€鍚湇鍔″櫒
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public bool StartServer(int port)
    {
        if (netManager.IsRunning)
        {
            StopImmediate();
        }
        try
        {
            isStopping = false;
            StartLocal();
            if (netManager.Start(port))
            {
                Log.Information($"寮€鍚湇鍔″櫒鎴愬姛锛岀鍙?{netManager.LocalPort}");
                Window.Frame += Update;
                Window.Closed += StopImmediate;
                listener.ConnectionRequestEvent += ConnectionRequestEvent;
                listener.NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
                listener.NetworkReceiveEvent += NetworkReceiveEvent;
                listener.PeerDisconnectedEvent += PeerDisconnectedEvent;
                netManager.PollEvents();
                CurrentStage = Stage.Connected;
                Server = Self;
                WorkType = WorkType.Server;
            }
            else
            {
                CurrentStage = Stage.NotConnected;
                Stop();
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
        return CurrentStage == Stage.Connected;
    }
    /// <summary>
    /// 杩炴帴鏈嶅姟鍣?
    /// </summary>
    /// <param name="ep"></param>
    /// <param name="passwd"></param>
    /// <returns></returns>
    public void ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null)
    {
        try
        {
            isStopping = false;
            Log.Information($"connecting to server at {ep.Address}");
            Clients.Clear();
            netManager.Start();
            TokenId = Guid.NewGuid();
            onlineAccessToken ??= SettingsManager.OnlineAccessToken;
            SendWriterFromPackage(new ConnectionRequestPackage(TokenId, ProjectNet.Server_Version, string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
            listener.NetworkReceiveUnconnectedEvent -= HandleConnectionReject;
            listener.NetworkReceiveEvent += NetworkReceiveEvent;
            listener.ConnectionRequestEvent += ConnectionRequestEvent;
            listener.NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
            listener.PeerDisconnectedEvent += PeerDisconnectedEvent;
            listener.NetworkErrorEvent += (obj, arg) =>
            {
                Log.Information("杩炴帴閿欒" + arg);
                Stop(arg.ToString());
            };
            CurrentStage = Stage.WaitForClientList;
            WorkType = WorkType.Client;
            Window.Frame += Update;
            Window.Closed += StopImmediate;
        }
        catch
        {
            CurrentStage = Stage.NotConnected;
        }
    }
    /// <summary>
    /// 鍙戦€佸洖璋冧簨浠?
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="userData"></param>
    public void DeliveryEvent(NetPeer peer, object userData)
    {
        if (userData is NetPeer netPeer && netPeer.Tag is Client client)
        {
            agreeOnPendingPeer.Remove(client.ID);
#if DEBUG
            Log.Information($"Client[{client.ID}]宸叉敹鍒癈lient[{pendingClient.ID}]鍔犲叆閫氱煡");
#endif
        }
        if (agreeOnPendingPeer.Count == 0 && pendingClient != null)
        {
            AddClient(pendingClient);
            QueuePackage(new ClientPackage(Clients.Values) { To = pendingClient });
#if DEBUG
            Log.Information($"Client[{pendingClient.ID}] join completed.");
#endif
            pendingPeer = null;
            pendingStartUtc = null;
        }
    }

    public void Stop(string error = null)
    {
        isStopping = true;
        Error = error;
    }
    public void StopImmediate()
    {
        try
        {
            if (pendingPeer != null) pendingPeer = null;
            agreeOnPendingPeer.Clear();
            pendingStartUtc = null;
            PendingPackages.Clear();
            // 鍏堝彂閫佹彁绀猴紝鐒跺悗鍐嶅叧闂湇鍔″櫒
            if (WorkType == WorkType.Server)
            {
                RemoveAllClients("Server is shutting down.");
            }
            if (netManager.IsRunning)
            {
                netManager.Stop(true);
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
        finally
        {
            Window.Closed -= StopImmediate;
            Window.Frame -= Update;
            listener.ClearConnectionRequestEvent();
            listener.ClearNetworkReceiveUnconnectedEvent();
            listener.ClearNetworkReceiveEvent();
            listener.ClearPeerDisconnectedEvent();
            listener.ClearDeliveryEvent();
            listener.ClearPeerConnectedEvent();
            OnRecieve = null;
            OnClientStateChanged = null;
            CurrentStage = Stage.NotConnected;
            WorkType = WorkType.Local;
            if (Error != null)
            {
                DialogsManager.Confirm(Error, btn =>
                {
                    ScreensManager.SwitchScreen("MainMenu");
                });
            }
        }
    }
    /// <summary>
    /// 寮€鍚寘澶勭悊
    /// </summary>
    public void TurnOnPackageHanlde(ProjectNet projectNet)
    {
        CommonLib.Net.OnRecieve += (node, list) =>
        {
            foreach (var c in list)
            {
                try
                {
                    c.Handle(projectNet, node, IsServer);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("[{0}]{1}", c.GetType().Name, e.Message));
                }
            }
        };
    }

    public void SendWriterFromPackage(IPackage package, IPEndPoint endPoint, bool isSend = true)
    {
        SendWriterFromPackages(new IPackage[] { package }, null, null, endPoint, isSend);
    }

    public void SendWriterFromPackage(IPackage package, NetPeer netPeer, bool useDeliveryEvent = false)
    {
        SendWriterFromPackages(new IPackage[] { package }, netPeer, null, null, useDeliveryEvent);
    }
    /// <summary>
    /// 鍙戦€佷竴涓暟鎹寘鍒拌繕鏈繛鎺ョ殑杩滅▼
    /// </summary>
    /// <param name="package">鏁版嵁鍖?/param>
    /// <param name="request">杩滅▼杩炴帴璇锋眰</param>
    /// <param name="reject">鍙戦€佸悗鏄惁鎷掔粷杩炴帴 true鎷掔粷 false涓嶆嫆缁?/param>
    public void SendWriterFromPackage(IPackage package, ConnectionRequest request, bool reject)
    {
        if (reject) request.Reject();
        SendWriterFromPackages(new IPackage[] { package }, null, request, null);
    }
    /// <summary>
    /// 鍙戦€佸涓暟鎹寘
    /// </summary>
    /// <param name="packages"></param>
    /// <param name="netPeer"></param>
    /// <param name="request"></param>
    /// <param name="iPEndPoint"></param>
    /// <param name="useDeliveryEvent">true 浣跨敤delivery浜嬩欢鍥炶皟 true 鍙戦€佹秷鎭?false 鍙戦€佽繛鎺ユ秷鎭?/param>
    public void SendWriterFromPackages(IEnumerable<IPackage> packages, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false)
    {
        PackageStreamWriter writer = new PackageStreamWriter();
        writer.IsServer = IsServer;
        writer.Project = ProjectNet.Project;
        foreach (var package in packages)
        {
            writer.Write(verifyByte);
            writer.Write(package.ID);
            package.WriteData(writer);
        }
        byte[] payload = writer.Data();
        SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public static int SendWriterFromPackage(NetManager netManager, IEnumerable<IPackage> packages, IPEndPoint endPoint)
    {
        PackageStreamWriter writer = new PackageStreamWriter();
        writer.IsServer = false;
        writer.Project = ProjectNet.Project;
        foreach (var packet in packages)
        {
            writer.Write(verifyByte);
            writer.Write(packet.ID);
            packet.WriteData(writer);
        }
        byte[] payload = writer.Data();
        int size = payload.Length;
        int chunkSize = SettingsManager.NetworkChunkSize;
        if (chunkSize < minChunkSize) chunkSize = minChunkSize;
        if (chunkSize > maxChunkSize) chunkSize = maxChunkSize;
        if (endPoint != null)
        {
            if (payload.Length <= chunkSize)
            {
                NetDataWriter w = new NetDataWriter();
                w.PutBytesWithLength(payload);
                netManager.SendUnconnectedMessage(w, endPoint);
            }
            else
            {
                int messageId = Interlocked.Increment(ref s_chunkMessageId);
                for (int offset = 0; offset < payload.Length; offset += chunkSize)
                {
                    int length = Math.Min(chunkSize, payload.Length - offset);
                    byte[] chunk = new byte[length];
                    Buffer.BlockCopy(payload, offset, chunk, 0, length);
                    NetDataWriter chunkWriter = new NetDataWriter();
                    chunkWriter.Put(chunkMagic);
                    chunkWriter.Put(chunkVersion);
                    chunkWriter.Put(messageId);
                    chunkWriter.Put(payload.Length);
                    chunkWriter.Put(offset);
                    chunkWriter.Put((ushort)length);
                    chunkWriter.Put(chunk);
                    netManager.SendUnconnectedMessage(chunkWriter, endPoint);
                }
            }
        }
        return size;
    }

    public void Update()
    {
        CleanupChunkStates();
        //鍏堝鐞哢I鎿嶄綔锛孖nventory搴忓彿澧炲姞鍚庡啀澶勭悊瀹㈡埛绔繃鏉ョ殑鍖?
        //鍙戦€佺墿鍝佸悓姝ユ暟鎹?
        SubsystemInventories.flushSyncItems();

        // 澶勭悊 pendingClient 绛夊緟绐楀彛锛氭竻鐞嗘棤鏁堝鎴风骞跺湪瓒呮椂鍚庡己鍒跺畬鎴愬姞鍏?
        if (pendingPeer != null)
        {
            // 绉婚櫎宸叉柇寮€鎴栦笉瀛樺湪鐨勫鎴风 ID锛岄伩鍏嶆案涔呯瓑寰?
            var toRemoveIds = new List<byte>();
            foreach (var id in agreeOnPendingPeer)
            {
                if (!Clients.ContainsKey(id) || !Clients[id].IsConnected)
                {
                    toRemoveIds.Add(id);
                }
            }
            foreach (var id in toRemoveIds)
            {
                agreeOnPendingPeer.Remove(id);
            }

            // 瓒呮椂澶勭悊锛氬鏋滆秴鏃朵粛鏈夋湭纭鐨勫鎴风锛岀洿鎺ュ畬鎴愬姞鍏ワ紝閬垮厤闃诲鍚庣画杩炴帴
            if (!pendingStartUtc.HasValue)
            {
                pendingStartUtc = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - pendingStartUtc.Value).TotalSeconds > PendingJoinTimeoutSeconds)
            {
                Log.Information($"绛夊緟瀹㈡埛绔洖搴旇秴鏃讹紝寮哄埗瀹屾垚 Client[{pendingClient?.ID}] 鍔犲叆銆傚墿浣欑瓑寰呮暟閲? {agreeOnPendingPeer.Count}");
                // 鐩存帴瀹屾垚鍔犲叆娴佺▼
                if (pendingClient != null)
                {
                    AddClient(pendingClient);
                    QueuePackage(new ClientPackage(Clients.Values) { To = pendingClient });
                }
                pendingPeer = null;
                agreeOnPendingPeer.Clear();
                pendingStartUtc = null;
            }
            else if (agreeOnPendingPeer.Count == 0 && pendingClient != null)
            {
                // 娌℃湁闇€瑕佺瓑寰呯殑瀹㈡埛绔紝琛ュ厖涓€娆″畬鎴愰€昏緫锛堜笌 DeliveryEvent 淇濇寔涓€鑷达級
                AddClient(pendingClient);
                QueuePackage(new ClientPackage(Clients.Values) { To = pendingClient });
                pendingPeer = null;
                pendingStartUtc = null;
            }
        }

        foreach (var pair in ClientsToRemove)
        {
            RemoveClientImmediate(pair.Key, pair.Value);
        }
        ClientsToRemove.Clear();
        //鎵瑰鐞嗗彂閫佸寘闃熷垪锛?.05s鍙戦€佷竴娆?
        if (PendingPackages.Count > 0 && Time.PeriodicEvent(0.05, 0.0))
        {
            lock (PendingPackages)
            {
                if (WorkType == WorkType.Client)
                {
                    if (Clients.Count > 0)
                    {
                        var c = Clients[0];
                        PackageStreamWriter writer = new PackageStreamWriter();
                        writer.IsServer = IsServer;
                        writer.Project = ProjectNet.Project;
                        bool hasWrite = false; int count = 0;
                        foreach (var packet in PendingPackages)
                        {
                            if (packet.To != null && packet.To != c) continue;
                            if (packet.Except != null && packet.Except == c) continue;
                            writer.Write(verifyByte);
                            writer.Write(packet.ID);
                            packet.WriteData(writer);
                            hasWrite = true;
                            count++;
                        }
                        if (hasWrite)
                        {
                            byte[] payload = writer.Data();
                            SendPayload(payload, c.Peer);
                            sec_length += payload.Length;
                        }
                    }
                }
                else
                {
                    foreach (var c in Clients.Values)
                    {
                        if (c.IsConnected)
                        {
                            PackageStreamWriter writer = new PackageStreamWriter();
                            writer.IsServer = IsServer;
                            writer.Project = ProjectNet.Project;
                            bool hasWrite = false; int count = 0;
                            foreach (var packet in PendingPackages)
                            {
                                if (packet.To != null && packet.To != c) continue;
                                if (packet.Except != null && packet.Except == c) continue;
                                if (c.State >= packet.MinNeedState || WorkType == WorkType.Client)
                                {
                                    writer.Write(verifyByte);
                                    writer.Write(packet.ID);
                                    packet.WriteData(writer);
                                    hasWrite = true;
                                    count++;
                                }
                            }
                            if (hasWrite)
                            {
                                byte[] payload = writer.Data();
                                SendPayload(payload, c.Peer);
                                sec_length += payload.Length;
#if DEBUG
                                //Log.Information($"鏈鍙戦€佸寘{count}涓紝{size}瀛楄妭");
#endif
                            }
                        }
                    }
                }



#if NETSTAT
                if (Time.PeriodicEvent(1.0, 0.0))
                {
                    sec_length_total += sec_length;
                    sec_total++;
                    float f = (sec_length / 1024f);
                    float f2 = (sec_length_total / 1024f);
                    sec_length = 0;
                    Log.Information($"1s鍐呯疮璁″彂閫佸寘闀垮害锛歿f:0.000}kb /// {sec_total}s鍐呯疮璁″彂閫佸寘闀垮害锛歿f2:0.000}kb");
                }
#endif

                PendingPackages.Clear();
            }
        }

        if (isStopping)
        {
            StopImmediate();
        }
        else
        {
            netManager.PollEvents();
        }
    }

    public static bool IsValidIPAddress(string ip) {
        return IPAddress.TryParse(ip, out _);
    }

    public static bool IsValidPort(int port) {
        return port > 0 && port <= 65535;
    }
}



