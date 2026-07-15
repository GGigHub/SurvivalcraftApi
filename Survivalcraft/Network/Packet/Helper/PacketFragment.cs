using System;
using System.Collections.Generic;
using System.Threading;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public sealed class PacketFragment {
    internal const byte VerifyByte = 0x88;
    internal const uint ChunkMagic = 0x4E46434B;
    internal const byte ChunkVersion = 2;
    internal const int MinChunkSize = 256;
    internal const int MaxChunkSize = 60 * 1024;
    private const int ChunkHeaderBytes = 19;
    private const double ChunkStateTimeoutSeconds = 15.0;

    private static int s_chunkMessageId = 1;

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

    public bool TryProcessIncoming(NetDataReader reader, string sourceKey, out byte[] payload) {
        payload = null;
        if (reader == null || reader.AvailableBytes == 0) return false;

        // Peek to see if it's a chunk frame
        if (reader.AvailableBytes >= 4) {
            var probe = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
            if (probe.GetUInt() == ChunkMagic) {
                // It is a chunk
                if (!TryReadChunkFrame(reader, out int messageId, out int totalLength, out int offset, out byte[] chunkData)) {
                    return false; // Invalid chunk
                }
                return TryAssembleChunk(sourceKey, messageId, totalLength, offset, chunkData, out payload);
            }
        }

        // Not a chunk frame, it's a direct payload wrapped with PutBytesWithLength
        try {
            payload = reader.GetBytesWithLength();
            return payload != null;
        } catch {
            return false;
        }
    }

    public static IEnumerable<NetDataWriter> FragmentPayload(byte[] payload) {
        if (payload == null) {
            yield break;
        }

        int chunkSize = GetConfiguredChunkSize();
        if (payload.Length <= chunkSize) {
            yield return BuildPayloadWriter(payload);
            yield break;
        }

        int messageId = Interlocked.Increment(ref s_chunkMessageId);
        for (int offset = 0; offset < payload.Length; offset += chunkSize) {
            yield return BuildChunkWriter(messageId, payload.Length, offset, payload, chunkSize);
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

        if (reader.GetUInt() != ChunkMagic) {
            return false;
        }

        byte version = reader.GetByte();
        if (version != ChunkVersion) {
            throw new InvalidOperationException($"Invalid chunk frame version: {version}, expected {ChunkVersion}.");
        }

        messageId = reader.GetInt();
        totalLength = reader.GetInt();
        offset = reader.GetInt();
        int length = reader.GetUShort();
        
        if (length <= 0 || totalLength <= 0 || offset < 0 || offset + length > totalLength) {
            throw new InvalidOperationException($"Invalid chunk frame payload. totalLength={totalLength}, offset={offset}, length={length}.");
        }

        byte[] body = reader.GetBytesWithLength();
        if (body == null || body.Length != length) {
            throw new InvalidOperationException($"Invalid chunk frame body length. declared={length}, actual={(body == null ? -1 : body.Length)}.");
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
            throw new InvalidOperationException($"Chunk state mismatch for key={key}.");
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
}
