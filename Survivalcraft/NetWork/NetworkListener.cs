using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork {
    public class EventBasedNetListener : INetEventListener {
        public event OnNetworkReceive NetworkReceiveEvent;

        public delegate void OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod);
        public event Action<NetPeer> PeerConnectedEvent;
        public event Action<NetPeer, DisconnectInfo> PeerDisconnectedEvent;
        public event Action<IPEndPoint, NetPacketReader, UnconnectedMessageType> NetworkReceiveUnconnectedEvent;
        public event Action<byte[], IPEndPoint> NetworkErrorEvent;
        public event Action<ConnectionRequest> ConnectionRequestEvent;
        public event Action<NetPeer, object> DeliveryEvent;

        public EventBasedNetListener() {
        }

        public void ClearNetworkReceiveEvent() {
            NetworkReceiveEvent = null;
        }

        public void ClearPeerConnectedEvent() {
            PeerConnectedEvent = null;
        }

        public void ClearPeerDisconnectedEvent() {
            PeerDisconnectedEvent = null;
        }

        public void ClearNetworkReceiveUnconnectedEvent() {
            NetworkReceiveUnconnectedEvent = null;
        }

        public void ClearNetworkErrorEvent() {
            NetworkErrorEvent = null;
        }

        public void ClearConnectionRequestEvent() {
            ConnectionRequestEvent = null;
        }

        public void ClearDeliveryEvent() {
            // 不直接操作事件，而是通过其他方式处理
            // 在实际应用中，可以通过生命周期管理来处理事件订阅
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) {
            PeerConnectedEvent?.Invoke(peer);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            PeerDisconnectedEvent?.Invoke(peer, disconnectInfo);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
            // 将NetPacketReader的数据复制到NetDataReader来兼容现有代码
            var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
            NetworkReceiveEvent?.Invoke(peer, dataReader, deliveryMethod);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
            NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) {
            NetworkErrorEvent?.Invoke(null, endPoint); // 简单处理，实际可能需要传递socketError信息
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) {
            ConnectionRequestEvent?.Invoke(request);
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) {
            // 暂不处理延迟更新
        }

    }

}