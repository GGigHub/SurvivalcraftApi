using System.Buffers.Binary;
using System.Text.Json;

namespace Survivalcraft.Testing.Control;

public static class LengthPrefixedJsonStream {
    private const int HeaderLength = sizeof(int);

    public static async ValueTask WriteAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, ControlProtocol.JsonOptions);
        if (payload.Length > ControlProtocol.MaximumFrameBytes) {
            throw new ControlProtocolException(
                "frame_too_large",
                $"JSON frame size {payload.Length} exceeds {ControlProtocol.MaximumFrameBytes} bytes.");
        }

        byte[] header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<T?> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
        where T : class {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[HeaderLength];
        int headerBytes = await ReadAtMostAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0) {
            return null;
        }
        if (headerBytes != HeaderLength) {
            throw new EndOfStreamException("The control frame ended inside its length header.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength <= 0 || payloadLength > ControlProtocol.MaximumFrameBytes) {
            throw new ControlProtocolException(
                "invalid_frame_length",
                $"Control frame length {payloadLength} is outside the allowed range.");
        }

        byte[] payload = new byte[payloadLength];
        int payloadBytes = await ReadAtMostAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != payloadLength) {
            throw new EndOfStreamException(
                $"The control frame ended after {payloadBytes} of {payloadLength} payload bytes.");
        }

        return JsonSerializer.Deserialize<T>(payload, ControlProtocol.JsonOptions)
            ?? throw new ControlProtocolException(
                "invalid_json",
                $"The control frame did not contain a valid {typeof(T).Name} value.");
    }

    private static async ValueTask<int> ReadAtMostAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken) {
        int total = 0;
        while (total < buffer.Length) {
            int read = await stream.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken).ConfigureAwait(false);
            if (read == 0) {
                break;
            }
            total += read;
        }
        return total;
    }
}
