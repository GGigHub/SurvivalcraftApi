using LiteNetLib;
using Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    public int ClientCount
    {
        get
        {

            return Peers.Count() + 1;
        }
    }

    public Dictionary<int, Client> Clients = new Dictionary<int, Client>();

    public NetPeer pendingPeer = null;
    // 只有这些客户端同意以后新客户端才能加入
    public HashSet<byte> agreeOnPendingPeer = new HashSet<byte>();

    private Client pendingClient => (Client)pendingPeer?.Tag;

    public IEnumerable<Client> Peers => Clients.Values.Where(c => c != Self);

    // 控制新客户端加入的等待窗口，防止旧客户端 DeliveryEvent 丢失导致一直卡在 pending 状态
    private DateTime? pendingStartUtc;
    private const double PendingJoinTimeoutSeconds = 2.0; // 超时自动完成加入，单位秒

    /// <summary>
    /// 包发送队列
    /// </summary>
    private List<IPackage> PendingPackages = new List<IPackage>();
    /// <summary>
    /// 包处理队列
    /// </summary>
    private List<IPackage> PendingHanldePackages = new List<IPackage>();

    // 当一个客户端收到一个消息
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

    //添加到队列
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
        throw new Exception("服务器连接人数已满");
    }


    // 创建客户
    public Client CreateClient(NetPeer peer, Guid tokenId, Guid guid, string dataId, string nickname)
    {
        if (!IsServer) throw new InvalidOperationException("creating client as non-server");
        foreach (var p in Peers)
        {
            //下一帧移除，否则会报错集合以更改
            if (p.PlayerGuid == guid) RemoveClient(p, "账号从其它设备登录");
        }
        var c = new Client(peer, FindUnusedIndex(), tokenId, guid, dataId, nickname);
        peer.Tag = c;
        return c;
    }

    public void AddClient(Client c)
    {
#if SERVER
        // 伪崩服的触发原因是客户端发现Clients字典中有和自己相同的Guid和ClientID
        // 但是客户端并不需要负责玩家的异常退出部分
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
                RemoveClient(Clients[k], "玩家从其他地方登录");
            }
            if (Clients.ContainsKey(c.ID))
            {
                RemoveClient(Clients[c.ID], "检测到重复的ClientID，请重新连接");
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

    // 删除客户
    public void RemoveClientImmediate(Client c, string reason = null)
    {
        if (c == Self)
        {
            // 如果是自己，停止服务器
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
    /// 服务器关闭，不执行OnClientStateChanged，防止多次Dispose
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
    /// 连接断开事件 服务端
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="disconnectInfo"></param>
    private void PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log.Information($"{peer.Address}连接关闭: " + disconnectInfo.Reason);
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
                //同步Client列表
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
    /// 连接断开事件 客户端
    /// </summary>
    /// <param name="remoteEndPoint"></param>
    /// <param name="reader"></param>
    /// <param name="messageType"></param>
    private void UnconnectedReceiveEvent(IPEndPoint remoteEndPoint, NetPacketReader reader, bool fromBroadcast)
    {
        if (CurrentStage == Stage.NotConnected) return;
        var list = PackageManager.DecodePackages(this, reader, null, null, remoteEndPoint);
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
        Log.Information("[排队]添加到处理队列：" + package.ID);
#endif
        PendingHanldePackages.Add(package);
    }
    /// <summary>
    /// 数据接收事件
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="reader"></param>
    /// <param name="deliveryMethod"></param>
    public void NetworkReceiveEvent(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod)
    {
        if (CurrentStage == Stage.NotConnected) return;
        var fromClient = peer.Tag as Client;
        var list = PackageManager.DecodePackages(this, reader, peer);
        list.InsertRange(0, PendingHanldePackages);
        PendingHanldePackages.Clear();
        try
        {
            if (OnRecieve == null)
            {
#if DEBUG
                Log.Information("检测到有未处理的包:" + list.Count);
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
            // 注意：NetDataReader不需要Recycle()方法
        }
    }
    /// <summary>
    /// 连接被拒绝事件
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
    /// 新客户端连接事件
    /// </summary>
    /// <param name="request"></param>
    private void ConnectionRequestEvent(ConnectionRequest request)
    {
        //long now = CommonLib.GetMicroSeconds();
        //if (now - m_lastConnectTime < 10000)
        //{
        //    SendWriterFromPackage(new ConnectionRejectPackage("当前连接较多，请等待1秒加入"), request, true);
        //    return;
        //}
        //else
        //{
        //    m_lastConnectTime = CommonLib.GetMicroSeconds();
        //}
        Log.Information("接收到连接请求 " + request.RemoteEndPoint);



        if (ProjectNet.Project == null)
        {
            SendWriterFromPackage(new ConnectionRejectPackage("请等待服务器启动完成后加入"), request, true);
        }
        else if (pendingClient != null)
        {
            Log.Information($"Too Many Request And Reject Current Request:{request.RemoteEndPoint}");
            Log.Information($"Current Pending Client:{pendingClient}");
            SendWriterFromPackage(new ConnectionRejectPackage($"当前服务器连接请求太多，请稍候再试，等待客户端回应数{agreeOnPendingPeer.Count}"), request, true);
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
                SendWriterFromPackage(new ConnectionRejectPackage($"错误的连接请求数据包:{e.Message}"), request, true);
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
    /// 开启服务器
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
                Log.Information($"开启服务器成功，端口 {netManager.LocalPort}");
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
    /// 连接服务器
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
                Log.Information("连接错误" + arg);
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
    /// 发送回调事件
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="userData"></param>
    public void DeliveryEvent(NetPeer peer, object userData)
    {
        if (userData is NetPeer netPeer && netPeer.Tag is Client client)
        {
            agreeOnPendingPeer.Remove(client.ID);
#if DEBUG
            Log.Information($"Client[{client.ID}]已收到Client[{pendingClient.ID}]加入通知");
#endif
        }
        if (agreeOnPendingPeer.Count == 0 && pendingClient != null)
        {
            AddClient(pendingClient);
            QueuePackage(new ClientPackage(Clients.Values) { To = pendingClient });
#if DEBUG
            Log.Information($"Client[{pendingClient.ID}]已完成加入过程");
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
            // 先发送提示，然后再关闭服务器
            if (WorkType == WorkType.Server)
            {
                RemoveAllClients("服务器主动关闭");
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
    /// 开启包处理
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
    /// 发送一个数据包到还未连接的远程
    /// </summary>
    /// <param name="package">数据包</param>
    /// <param name="request">远程连接请求</param>
    /// <param name="reject">发送后是否拒绝连接 true拒绝 false不拒绝</param>
    public void SendWriterFromPackage(IPackage package, ConnectionRequest request, bool reject)
    {
        if (reject) request.Reject();
        SendWriterFromPackages(new IPackage[] { package }, null, request, null);
    }
    /// <summary>
    /// 发送多个数据包
    /// </summary>
    /// <param name="packages"></param>
    /// <param name="netPeer"></param>
    /// <param name="request"></param>
    /// <param name="iPEndPoint"></param>
    /// <param name="useDeliveryEvent">true 使用delivery事件回调 true 发送消息 false 发送连接消息</param>
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
        var w = CommonLib.GetWriter(writer, out int size);
        if (netPeer != null) {
            // In newer LiteNetLib versions, we may need to handle delivery events differently
            netPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }
        else if (request != null) {
            netManager.SendUnconnectedMessage(w, request.RemoteEndPoint);
        }
        else if (iPEndPoint != null) {
            if (!useDeliveryEvent) netManager.Connect(iPEndPoint, w);
            else netManager.SendUnconnectedMessage(w, iPEndPoint);
        }
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
        var w = CommonLib.GetWriter(writer, out int size);
        if (endPoint != null) netManager.SendUnconnectedMessage(w, endPoint);
        return size;
    }

    public void Update()
    {
        //先处理UI操作，Inventory序号增加后再处理客户端过来的包
        //发送物品同步数据
        SubsystemInventories.flushSyncItems();

        // 处理 pendingClient 等待窗口：清理无效客户端并在超时后强制完成加入
        if (pendingPeer != null)
        {
            // 移除已断开或不存在的客户端 ID，避免永久等待
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

            // 超时处理：如果超时仍有未确认的客户端，直接完成加入，避免阻塞后续连接
            if (!pendingStartUtc.HasValue)
            {
                pendingStartUtc = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - pendingStartUtc.Value).TotalSeconds > PendingJoinTimeoutSeconds)
            {
                Log.Information($"等待客户端回应超时，强制完成 Client[{pendingClient?.ID}] 加入。剩余等待数量: {agreeOnPendingPeer.Count}");
                // 直接完成加入流程
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
                // 没有需要等待的客户端，补充一次完成逻辑（与 DeliveryEvent 保持一致）
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
        //批处理发送包队列，0.05s发送一次
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
                            c.Peer.Send(CommonLib.GetWriter(writer, out int size), DeliveryMethod.ReliableOrdered);
                            sec_length += size;
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
                                c.Peer.Send(CommonLib.GetWriter(writer, out int size), DeliveryMethod.ReliableOrdered);
                                sec_length += size;
#if DEBUG
                                //Log.Information($"本次发送包{count}个，{size}字节");
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
                    Log.Information($"1s内累计发送包长度：{f:0.000}kb /// {sec_total}s内累计发送包长度：{f2:0.000}kb");
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
