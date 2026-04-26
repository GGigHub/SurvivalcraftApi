using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

internal sealed class NetworkFragment {
    internal const byte VerifyByte = 0x88;
    internal const uint ChunkMagic = 0x4E46434B;
    internal const byte ChunkVersion = 2;
    internal const int MinChunkSize = 256;
    internal const int MaxChunkSize = 60 * 1024;
    private const int ChunkHeaderBytes = 19;
    private const double ChunkStateTimeoutSeconds = 15.0;

    private static int s_chunkMessageId = 1;

    private readonly INetworkNode m_node;
    private readonly Dictionary<string, ChunkState> m_chunkStates = new Dictionary<string, ChunkState>();
    private double m_lastCleanupTime;

    private sealed class ChunkState {
        public int TotalLength;
        public int ChunkSize;
        public int TotalChunks;
        public int ReceivedChunks;
        public DateTime LastUpdateUtc;
        public byte[] Buffer;
        public bool[] Received;
    }

    public NetworkFragment(INetworkNode node) {
        m_node = node;
    }

    public void CleanupStates() {
        double now = Time.RealTime;
        if (now - m_lastCleanupTime < 1.0) {
            return;
        }
        m_lastCleanupTime = now;
        DateTime utcNow = DateTime.UtcNow;
        var expiredKeys = new List<string>();
        foreach (var pair in m_chunkStates) {
            if ((utcNow - pair.Value.LastUpdateUtc).TotalSeconds > ChunkStateTimeoutSeconds) {
                expiredKeys.Add(pair.Key);
            }
        }
        foreach (string key in expiredKeys) {
            m_chunkStates.Remove(key);
        }
    }

    public bool TryReadPayload(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out byte[] payload, out bool consumedChunkFrame) {
        payload = null;
        consumedChunkFrame = false;
        if (!TryReadChunkFrame(reader, out int messageId, out int totalLength, out int offset, out byte[] chunkData)) {
            return false;
        }

        consumedChunkFrame = true;
        string sourceKey = GetSourceKey(peer, request, endPoint);
        if (chunkData == null || !TryAssembleChunk(sourceKey, messageId, totalLength, offset, chunkData, out payload)) {
            return true;
        }
        return true;
    }

    public void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        if (payload == null) {
            return;
        }

        int chunkSize = GetConfiguredChunkSize();
        bool connectPath = iPEndPoint != null && !useDeliveryEvent;
        if (payload.Length <= chunkSize || connectPath) {
            var writer = BuildPayloadWriter(payload);
            if (netPeer != null) {
                netPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else if (request != null) {
                m_node.netManager.SendUnconnectedMessage(writer, request.RemoteEndPoint);
            }
            else if (iPEndPoint != null) {
                if (!useDeliveryEvent) m_node.netManager.Connect(iPEndPoint, writer);
                else m_node.netManager.SendUnconnectedMessage(writer, iPEndPoint);
            }
            return;
        }

        int messageId = Interlocked.Increment(ref s_chunkMessageId);
        for (int offset = 0; offset < payload.Length; offset += chunkSize) {
            var chunkWriter = BuildChunkWriter(messageId, payload.Length, offset, payload, chunkSize);
            if (netPeer != null) {
                netPeer.Send(chunkWriter, DeliveryMethod.ReliableOrdered);
            }
            else if (request != null) {
                m_node.netManager.SendUnconnectedMessage(chunkWriter, request.RemoteEndPoint);
            }
            else if (iPEndPoint != null) {
                m_node.netManager.SendUnconnectedMessage(chunkWriter, iPEndPoint);
            }
        }
    }

    public static void SendPayload(NetManager netManager, byte[] payload, IPEndPoint endPoint) {
        if (payload == null || endPoint == null) {
            return;
        }

        int chunkSize = GetConfiguredChunkSize();
        if (payload.Length <= chunkSize) {
            NetDataWriter writer = new NetDataWriter();
            writer.PutBytesWithLength(payload);
            netManager.SendUnconnectedMessage(writer, endPoint);
            return;
        }

        int messageId = Interlocked.Increment(ref s_chunkMessageId);
        for (int offset = 0; offset < payload.Length; offset += chunkSize) {
            var chunkWriter = BuildChunkWriter(messageId, payload.Length, offset, payload, chunkSize);
            netManager.SendUnconnectedMessage(chunkWriter, endPoint);
        }
    }

    private bool TryReadChunkFrame(NetDataReader reader, out int messageId, out int totalLength, out int offset, out byte[] chunkData) {
        messageId = 0;
        totalLength = 0;
        offset = 0;
        chunkData = null;
        if (reader == null || reader.AvailableBytes < ChunkHeaderBytes) {
            return false;
        }

        var probe = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        if (probe.GetUInt() != ChunkMagic) {
            return false;
        }

        byte version = probe.GetByte();
        if (version != ChunkVersion) {
            throw new InvalidOperationException($"Invalid chunk frame version: {version}, expected {ChunkVersion}.");
        }

        messageId = probe.GetInt();
        totalLength = probe.GetInt();
        offset = probe.GetInt();
        int length = probe.GetUShort();
        if (length <= 0 || totalLength <= 0 || offset < 0 || offset + length > totalLength) {
            throw new InvalidOperationException($"Invalid chunk frame payload. totalLength={totalLength}, offset={offset}, length={length}, available={probe.AvailableBytes}.");
        }

        byte[] body = probe.GetBytesWithLength();
        if (body == null || body.Length != length) {
            throw new InvalidOperationException($"Invalid chunk frame body length. declared={length}, actual={(body == null ? -1 : body.Length)}, available={probe.AvailableBytes}.");
        }
        chunkData = body;
        return true;
    }

    private bool TryAssembleChunk(string sourceKey, int messageId, int totalLength, int offset, byte[] chunkData, out byte[] payload) {
        payload = null;
        if (chunkData == null || chunkData.Length == 0) {
            throw new InvalidOperationException("Chunk frame has empty chunkData.");
        }

        int chunkSize = GetConfiguredChunkSize();
        int totalChunks = (totalLength + chunkSize - 1) / chunkSize;
        if (totalChunks <= 0) {
            throw new InvalidOperationException($"Invalid totalChunks computed from totalLength={totalLength}, chunkSize={chunkSize}.");
        }

        int index = offset / chunkSize;
        if (index < 0 || index >= totalChunks) {
            throw new InvalidOperationException($"Invalid chunk index={index}, totalChunks={totalChunks}, offset={offset}, chunkSize={chunkSize}.");
        }

        string key = BuildChunkStateKey(sourceKey, messageId);
        if (!m_chunkStates.TryGetValue(key, out ChunkState state)) {
            state = new ChunkState {
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
        else if (state.TotalLength != totalLength || state.ChunkSize != chunkSize || state.TotalChunks != totalChunks) {
            m_chunkStates.Remove(key);
            throw new InvalidOperationException($"Chunk state mismatch for key={key}. state(total={state.TotalLength},size={state.ChunkSize},chunks={state.TotalChunks}) incoming(total={totalLength},size={chunkSize},chunks={totalChunks}).");
        }

        if (!state.Received[index]) {
            Buffer.BlockCopy(chunkData, 0, state.Buffer, offset, chunkData.Length);
            state.Received[index] = true;
            state.ReceivedChunks++;
        }
        state.LastUpdateUtc = DateTime.UtcNow;

        if (state.ReceivedChunks >= state.TotalChunks) {
            payload = state.Buffer;
            m_chunkStates.Remove(key);
            return true;
        }

        return false;
    }

    private static int GetConfiguredChunkSize() {
        int configured = SettingsManager.NetworkChunkSize;
        if (configured < MinChunkSize) return MinChunkSize;
        if (configured > MaxChunkSize) return MaxChunkSize;
        return configured;
    }

    private static NetDataWriter BuildPayloadWriter(byte[] payload) {
        NetDataWriter writer = new NetDataWriter();
        writer.PutBytesWithLength(payload);
        return writer;
    }

    private static NetDataWriter BuildChunkWriter(int messageId, int totalLength, int offset, byte[] payload, int chunkSize) {
        int length = Math.Min(chunkSize, totalLength - offset);
        byte[] chunk = new byte[length];
        Buffer.BlockCopy(payload, offset, chunk, 0, length);
        NetDataWriter writer = new NetDataWriter();
        writer.Put(ChunkMagic);
        writer.Put(ChunkVersion);
        writer.Put(messageId);
        writer.Put(totalLength);
        writer.Put(offset);
        writer.Put((ushort)length);
        writer.PutBytesWithLength(chunk);
        return writer;
    }

    private static string BuildChunkStateKey(string sourceKey, int messageId) {
        return $"{sourceKey}:{messageId}";
    }

    private static string GetSourceKey(NetPeer peer, ConnectionRequest request, IPEndPoint endPoint) {
        if (peer != null) return "peer:" + peer.Address;
        if (request != null) return "req:" + request.RemoteEndPoint;
        if (endPoint != null) return "ep:" + endPoint;
        return "unknown";
    }
}
