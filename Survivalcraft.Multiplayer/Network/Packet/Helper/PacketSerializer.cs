using Engine;
using Game.Network;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public sealed class PacketSerializer {
    private static readonly System.Text.Encoding StrictUtf8 =
        new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    private readonly PacketStreamWriter m_writer;
    private readonly PacketStreamReader m_reader;

    public bool IsReading => m_reader != null;
    public bool IsWriting => m_writer != null;

    public PacketSerializer(PacketStreamWriter writer) {
        m_writer = writer;
    }

    public PacketSerializer(PacketStreamReader reader) {
        m_reader = reader;
    }

    public void Value(ref bool value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadBoolean();
    }

    public void Value(ref byte value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadByte();
    }

    public void Value(ref int value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadInt32();
    }

    public void Value(ref long value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadInt64();
    }

    public void Value(ref float value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadSingle();
    }

    public void Value(ref Guid value) {
        if (IsWriting) {
            byte[] data = value.ToByteArray();
            m_writer.Write((byte)data.Length);
            m_writer.Write(data);
        }
        else {
            byte len = m_reader.ReadByte();
            value = new Guid(m_reader.ReadBytes(len));
        }
    }

    public void Value(ref ushort value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadUInt16();
    }

    public void Value(ref double value) {
        if (IsWriting) m_writer.Write(value);
        else value = m_reader.ReadDouble();
    }

    public void Value(ref string value) {
        if (IsWriting) m_writer.Write(value ?? string.Empty);
        else value = m_reader.ReadString();
    }

    public void Value(ref string value, int maximumUtf8Bytes) {
        if (maximumUtf8Bytes < 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes));
        }
        if (IsWriting) {
            string text = value ?? string.Empty;
            int byteCount = StrictUtf8.GetByteCount(text);
            if (byteCount > maximumUtf8Bytes) {
                throw new InvalidOperationException(
                    $"String length {byteCount} bytes exceeds maximum {maximumUtf8Bytes}.");
            }
            m_writer.Write(text);
        }
        else {
            int byteCount = m_reader.Read7BitEncodedInt();
            if (byteCount < 0 || byteCount > maximumUtf8Bytes) {
                throw new InvalidOperationException(
                    $"String length {byteCount} bytes exceeds maximum {maximumUtf8Bytes}.");
            }
            byte[] bytes = m_reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount) {
                throw new EndOfStreamException(
                    $"Expected {byteCount} string bytes, received {bytes.Length}.");
            }
            value = StrictUtf8.GetString(bytes);
        }
    }

    public void Value(ref Color value) {
        if (IsWriting) {
            m_writer.Write(value.R);
            m_writer.Write(value.G);
            m_writer.Write(value.B);
            m_writer.Write(value.A);
        }
        else value = new Color(m_reader.ReadByte(), m_reader.ReadByte(), m_reader.ReadByte(), m_reader.ReadByte());
    }

    public void Value(ref Point3 value) {
        if (IsWriting) {
            m_writer.Write(value.X);
            m_writer.Write(value.Y);
            m_writer.Write(value.Z);
        }
        else value = new Point3(m_reader.ReadInt32(), m_reader.ReadInt32(), m_reader.ReadInt32());
    }

    public void Value(ref Point2 value) {
        if (IsWriting) {
            m_writer.Write(value.X);
            m_writer.Write(value.Y);
        }
        else value = new Point2(m_reader.ReadInt32(), m_reader.ReadInt32());
    }

    public void Value(ref Quaternion value) {
        if (IsWriting) {
            m_writer.Write(value.X);
            m_writer.Write(value.Y);
            m_writer.Write(value.Z);
            m_writer.Write(value.W);
        }
        else value = new Quaternion(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle());
    }

    public void Value(ref Vector3 value) {
        if (IsWriting) {
            m_writer.Write(value.X);
            m_writer.Write(value.Y);
            m_writer.Write(value.Z);
        }
        else value = new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle());
    }

    public void Value(ref Vector3? value) {
        if (IsWriting) {
            m_writer.Write(value.HasValue);
            if (value.HasValue) {
                m_writer.Write(value.Value.X);
                m_writer.Write(value.Value.Y);
                m_writer.Write(value.Value.Z);
            }
        }
        else {
            if (m_reader.ReadBoolean()) value = new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle());
            else value = null;
        }
    }

    public void Value(ref Vector2 value) {
        if (IsWriting) {
            m_writer.Write(value.X);
            m_writer.Write(value.Y);
        }
        else value = new Vector2(m_reader.ReadSingle(), m_reader.ReadSingle());
    }

    public void Value(ref Vector2? value) {
        if (IsWriting) {
            m_writer.Write(value.HasValue);
            if (value.HasValue) {
                m_writer.Write(value.Value.X);
                m_writer.Write(value.Value.Y);
            }
        }
        else {
            if (m_reader.ReadBoolean()) value = new Vector2(m_reader.ReadSingle(), m_reader.ReadSingle());
            else value = null;
        }
    }

    public void Value(ref Ray3 value) {
        if (IsWriting) {
            m_writer.Write(value.Position.X);
            m_writer.Write(value.Position.Y);
            m_writer.Write(value.Position.Z);
            m_writer.Write(value.Direction.X);
            m_writer.Write(value.Direction.Y);
            m_writer.Write(value.Direction.Z);
        }
        else value = new Ray3(new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle()), new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle()));
    }

    public void Value(ref Ray3? value) {
        if (IsWriting) {
            m_writer.Write(value.HasValue);
            if (value.HasValue) {
                m_writer.Write(value.Value.Position.X);
                m_writer.Write(value.Value.Position.Y);
                m_writer.Write(value.Value.Position.Z);
                m_writer.Write(value.Value.Direction.X);
                m_writer.Write(value.Value.Direction.Y);
                m_writer.Write(value.Value.Direction.Z);
            }
        }
        else {
            if (m_reader.ReadBoolean()) value = new Ray3(new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle()), new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle()));
            else value = null;
        }
    }

    public void Buffer(ref byte[] value) {
        Buffer(ref value, int.MaxValue);
    }

    public void Buffer(ref byte[] value, int maximumLength) {
        if (maximumLength < 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }
        if (IsWriting) {
            byte[] buff = value ?? [];
            if (buff.Length > maximumLength) {
                throw new InvalidOperationException(
                    $"Buffer length {buff.Length} exceeds maximum {maximumLength}.");
            }
            m_writer.Write(buff.Length);
            m_writer.Write(buff);
        }
        else {
            int buffSize = m_reader.ReadInt32();
            if (buffSize < 0 || buffSize > maximumLength) {
                throw new InvalidOperationException(
                    $"Buffer length {buffSize} is outside 0..{maximumLength}.");
            }
            value = m_reader.ReadBytes(buffSize);
            if (value.Length != buffSize) {
                throw new EndOfStreamException(
                    $"Expected {buffSize} buffer bytes, received {value.Length}.");
            }
        }
    }
    public void Value(ref Matrix? value) {
        if (IsWriting) {
            m_writer.Write(value.HasValue);
            if (value.HasValue) {
                m_writer.Write(value.Value.M11);
                m_writer.Write(value.Value.M21);
                m_writer.Write(value.Value.M31);
                m_writer.Write(value.Value.M41);
                m_writer.Write(value.Value.M12);
                m_writer.Write(value.Value.M22);
                m_writer.Write(value.Value.M32);
                m_writer.Write(value.Value.M42);
                m_writer.Write(value.Value.M13);
                m_writer.Write(value.Value.M23);
                m_writer.Write(value.Value.M33);
                m_writer.Write(value.Value.M43);
                m_writer.Write(value.Value.M14);
                m_writer.Write(value.Value.M24);
                m_writer.Write(value.Value.M34);
                m_writer.Write(value.Value.M44);
            }
        }
        else {
            if (m_reader.ReadBoolean()) {
                Matrix matrix = new Matrix();
                matrix.M11 = m_reader.ReadSingle();
                matrix.M21 = m_reader.ReadSingle();
                matrix.M31 = m_reader.ReadSingle();
                matrix.M41 = m_reader.ReadSingle();
                matrix.M12 = m_reader.ReadSingle();
                matrix.M22 = m_reader.ReadSingle();
                matrix.M32 = m_reader.ReadSingle();
                matrix.M42 = m_reader.ReadSingle();
                matrix.M13 = m_reader.ReadSingle();
                matrix.M23 = m_reader.ReadSingle();
                matrix.M33 = m_reader.ReadSingle();
                matrix.M43 = m_reader.ReadSingle();
                matrix.M14 = m_reader.ReadSingle();
                matrix.M24 = m_reader.ReadSingle();
                matrix.M34 = m_reader.ReadSingle();
                matrix.M44 = m_reader.ReadSingle();
                value = matrix;
            }
            else value = null;
        }
    }

    public void Value(ref CellFace value) {
        if (IsWriting) {
            m_writer.Write(value.X);
            m_writer.Write((byte)value.Y);
            m_writer.Write(value.Z);
            m_writer.Write((byte)value.Face);
        }
        else value = new CellFace(m_reader.ReadInt32(), m_reader.ReadByte(), m_reader.ReadInt32(), m_reader.ReadByte());
    }

    public void EntityLoadList(ref EntityDataList value) {
        if (IsWriting) {
            ValuesDictionary vd = new ValuesDictionary();
            value.Save(vd);
            byte[] data = vd.ToMessagePack();
            m_writer.Write(data.Length);
            m_writer.Write(data);
        }
        else {
            int buffSize = m_reader.ReadInt32();
            byte[] messagePack = m_reader.ReadBytes(buffSize);
            ValuesDictionary vd = new ValuesDictionary();
            vd.ApplyOverridesUseMessagePack(messagePack);
            value = new EntityDataList(GameManager.Project.GameDatabase, vd, false);
        }
    }

    public void Value(ref TerrainRaycastResult? value) {
        if (IsWriting) {
            m_writer.Write(value.HasValue);
            if (value.HasValue) {
                m_writer.Write(value.Value.Ray.Position.X);
                m_writer.Write(value.Value.Ray.Position.Y);
                m_writer.Write(value.Value.Ray.Position.Z);
                m_writer.Write(value.Value.Ray.Direction.X);
                m_writer.Write(value.Value.Ray.Direction.Y);
                m_writer.Write(value.Value.Ray.Direction.Z);
                m_writer.Write(value.Value.Value);
                m_writer.Write(value.Value.CollisionBoxIndex);
                m_writer.Write(value.Value.Distance);
                m_writer.Write(value.Value.CellFace.X);
                m_writer.Write((byte)value.Value.CellFace.Y);
                m_writer.Write(value.Value.CellFace.Z);
                m_writer.Write((byte)value.Value.CellFace.Face);
            }
        }
        else {
            if (m_reader.ReadBoolean()) {
                var raycast = new TerrainRaycastResult();
                raycast.Ray = new Ray3(new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle()), new Vector3(m_reader.ReadSingle(), m_reader.ReadSingle(), m_reader.ReadSingle()));
                raycast.Value = m_reader.ReadInt32();
                raycast.CollisionBoxIndex = m_reader.ReadInt32();
                raycast.Distance = m_reader.ReadSingle();
                raycast.CellFace = new CellFace(m_reader.ReadInt32(), m_reader.ReadByte(), m_reader.ReadInt32(), m_reader.ReadByte());
                value = raycast;
            }
            else value = null;
        }
    }

    public void Raw(ref byte[] value, int count) {
        if (IsWriting) m_writer.Write(value, 0, count);
        else value = m_reader.ReadBytes(count);
    }

    public bool TryValue(ref bool value) {
        if (IsWriting) {
            m_writer.Write(value);
            return true;
        }
        try {
            value = m_reader.ReadBoolean();
            return true;
        }
        catch {
            return false;
        }
    }

    public bool TryValue(ref int value) {
        if (IsWriting) {
            m_writer.Write(value);
            return true;
        }
        try {
            value = m_reader.ReadInt32();
            return true;
        }
        catch {
            return false;
        }
    }

    public void Enum<T>(ref T value) where T : struct {
        if (IsWriting) m_writer.Write(Convert.ToByte(value));
        else value = (T)System.Enum.ToObject(typeof(T), (int)m_reader.ReadByte());
    }
}

public class PacketStreamReader : BinaryReader {
    public PacketStreamReader(byte[] CompressedData) : base(new MemoryStream()) {
        byte[] data = PacketStreamHelper.Decompress(CompressedData);
        BaseStream.Write(data, 0, data.Length);
        BaseStream.Position = 0L;
    }
}

public class PacketStreamWriter : BinaryWriter {
    public PacketStreamWriter() : base(new MemoryStream()) {
    }

    public byte[] Data() {
        Flush();
        BaseStream.Seek(0, SeekOrigin.Begin);
        return PacketStreamHelper.Compress(BaseStream);
    }
}

public class PacketStreamHelper {
    public static byte[] Compress(Stream input) {
        using MemoryStream output = new();
        using (System.IO.Compression.DeflateStream dstream = new(output, System.IO.Compression.CompressionLevel.Fastest, true)) {
            input.CopyTo(dstream);
        }
        return output.ToArray();
    }
    public static byte[] Decompress(byte[] input) {
        using MemoryStream inputStream = new(input);
        using System.IO.Compression.DeflateStream dstream = new(inputStream, System.IO.Compression.CompressionMode.Decompress);
        using MemoryStream output = new();
        dstream.CopyTo(output);
        return output.ToArray();
    }
}


