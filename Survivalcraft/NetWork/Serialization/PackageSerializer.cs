using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public sealed class PackageSerializer {
    private readonly PackageStreamWriter m_writer;
    private readonly PackageStreamReader m_reader;

    public bool IsReading => m_reader != null;
    public bool IsWriting => m_writer != null;

    public PackageSerializer(PackageStreamWriter writer) {
        m_writer = writer;
    }

    public PackageSerializer(PackageStreamReader reader) {
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
        if (IsWriting) {
            byte[] buff = value ?? [];
            m_writer.Write(buff.Length);
            m_writer.Write(buff);
        }
        else {
            int buffSize = m_reader.ReadInt32();
            value = m_reader.ReadBytes(buffSize);
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

    public void Enum<T>(ref T value) where T : struct {
        if (IsWriting) m_writer.Write(Convert.ToByte(value));
        else value = (T)System.Enum.ToObject(typeof(T), (int)m_reader.ReadByte());
    }
}

public class PackageStreamReader : BinaryReader {
    public PackageStreamReader(byte[] CompressedData) : base(new MemoryStream()) {
        byte[] data = NetworkManager.Decompress(CompressedData);
        BaseStream.Write(data, 0, data.Length);
        BaseStream.Position = 0L;
    }
}

public class PackageStreamWriter : BinaryWriter {
    public byte[] Data() {
        BaseStream.Seek(0, SeekOrigin.Begin);
        return NetworkManager.Compress(BaseStream);
    }
}
