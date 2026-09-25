using System.Buffers.Binary;
using System.IO.Compression;

namespace Game;

/// <summary>
///     Applies the compact copy/literal delta format used by Compat23 patch data.
/// </summary>
internal static class Compat23BinaryDelta {
    private static ReadOnlySpan<byte> Magic => "SC23DLT1"u8;
    private const int HeaderSize = 20;

    public static byte[] Apply(byte[] source, byte[] patch) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.Length < HeaderSize || !patch.AsSpan(0, Magic.Length).SequenceEqual(Magic)) {
            throw new InvalidDataException("Invalid Compat23 delta header.");
        }

        int sourceLength = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(8, 4));
        int targetLength = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(12, 4));
        int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(16, 4));
        if (sourceLength != source.Length
            || targetLength < 0
            || compressedLength < 0
            || patch.Length != HeaderSize + compressedLength) {
            throw new InvalidDataException("Compat23 delta lengths do not match the input.");
        }

        byte[] target = new byte[targetLength];
        using MemoryStream compressedStream = new(patch, HeaderSize, compressedLength, false);
        using DeflateStream commands = new(compressedStream, CompressionMode.Decompress);
        int targetOffset = 0;
        while (targetOffset < target.Length) {
            int opcode = commands.ReadByte();
            if (opcode < 0) {
                throw new InvalidDataException("Compat23 delta ended before the target was complete.");
            }
            int length;
            if (opcode == 0) {
                int sourceOffset = ReadInt32(commands);
                length = ReadInt32(commands);
                if (sourceOffset < 0 || length <= 0 || sourceOffset > source.Length - length) {
                    throw new InvalidDataException("Compat23 delta contains an invalid copy range.");
                }
                if (length > target.Length - targetOffset) {
                    throw new InvalidDataException("Compat23 delta exceeds the target length.");
                }
                Buffer.BlockCopy(source, sourceOffset, target, targetOffset, length);
            }
            else if (opcode == 1) {
                length = ReadInt32(commands);
                if (length <= 0 || length > target.Length - targetOffset) {
                    throw new InvalidDataException("Compat23 delta contains an invalid literal length.");
                }
                ReadExactly(commands, target.AsSpan(targetOffset, length));
            }
            else {
                throw new InvalidDataException($"Unknown Compat23 delta opcode [{opcode}].");
            }
            targetOffset += length;
        }

        if (commands.ReadByte() >= 0) {
            throw new InvalidDataException("Compat23 delta contains trailing commands.");
        }
        return target;
    }

    private static int ReadInt32(Stream stream) {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(stream, buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer) {
        while (!buffer.IsEmpty) {
            int read = stream.Read(buffer);
            if (read <= 0) {
                throw new InvalidDataException("Compat23 delta is truncated.");
            }
            buffer = buffer[read..];
        }
    }
}
