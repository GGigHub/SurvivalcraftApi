using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PackageStreamReader : BinaryReader {

    public PackageStreamReader(byte[] CompressedData) : base(new MemoryStream()) {
        byte[] data = NetworkManager.Decompress(CompressedData);
        BaseStream.Write(data, 0, data.Length);
        BaseStream.Position = 0L;
    }

    public Vector3 ReadVector3() {
        return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
    }

    public Vector3? ReadVector3Nullable() {
        if (ReadBoolean()) return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
        else return null;
    }

    public Vector2 ReadVector2() {
        return new Vector2(ReadSingle(), ReadSingle());
    }

    public Vector2? ReadVector2Nullable() {
        if (ReadBoolean()) return new Vector2(ReadSingle(), ReadSingle());
        else return null;
    }

    public virtual Guid ReadGuid() {
        byte len = ReadByte();
        return new Guid(ReadBytes(len));
    }

    public Point3 ReadPoint3() {
        return new Point3(ReadInt32(), ReadInt32(), ReadInt32());
    }

    public Point2 ReadPoint2() {
        return new Point2(ReadInt32(), ReadInt32());
    }

    public Quaternion ReadQuaternion() {
        return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    }

    public Ray3 ReadRay3() {
        return new Ray3(ReadVector3(), ReadVector3());
    }

    public Ray3? ReadRay3Nullable() {
        if (ReadBoolean()) return ReadRay3();
        else return null;
    }

    public Point3 ReadBlockPoint() {
        return new Point3(ReadInt32(), ReadByte(), ReadInt32());
    }

    public CellFace ReadCellFace() {
        Point3 point = ReadBlockPoint();
        return new CellFace(point.X, point.Y, point.Z, ReadByte());
    }

    public TerrainRaycastResult ReadTerrainRaycastResult() {
        var raycast = new TerrainRaycastResult();
        raycast.Ray = ReadRay3();
        raycast.Value = ReadInt32();
        raycast.CollisionBoxIndex = ReadInt32();
        raycast.Distance = ReadSingle();
        raycast.CellFace = ReadCellFace();
        return raycast;
    }

    public TerrainRaycastResult? ReadTerrainRaycastResultNullable() {
        if (ReadBoolean()) return ReadTerrainRaycastResult();
        else return null;
    }
    public ValuesDictionary ReadValuesDictionary() {
        byte[] data = ReadBytes(ReadInt32());
        var dict = new ValuesDictionary();
        dict.ApplyOverridesUseMessagePack(data);
        return dict;
    }

    public byte[] ReadBuff() {
        int buffSize = ReadInt32();
        var buff = new byte[buffSize];
        Read(buff, 0, buffSize);
        return buff;
    }

    public virtual Color ReadColor() {
        return new Color(ReadByte(), ReadByte(), ReadByte(), ReadByte());
    }

    public virtual T ReadEnum<T>() where T : struct {
        return (T)Enum.ToObject(typeof(T), (int)ReadByte());
    }

    public EntityDataList ReadEntityLoadList() {
        byte[] messagePack = ReadBuff();
        ValuesDictionary vd = new ValuesDictionary();
        vd.ApplyOverridesUseMessagePack(messagePack);
        return new EntityDataList(GameManager.Project.GameDatabase, vd, false);
    }

    public Matrix ReadMatrix() {
        Matrix matrix = new Matrix();
        matrix.M11 = ReadSingle();
        matrix.M21 = ReadSingle();
        matrix.M31 = ReadSingle();
        matrix.M41 = ReadSingle();

        matrix.M12 = ReadSingle();
        matrix.M22 = ReadSingle();
        matrix.M32 = ReadSingle();
        matrix.M42 = ReadSingle();

        matrix.M13 = ReadSingle();
        matrix.M23 = ReadSingle();
        matrix.M33 = ReadSingle();
        matrix.M43 = ReadSingle();

        matrix.M14 = ReadSingle();
        matrix.M24 = ReadSingle();
        matrix.M34 = ReadSingle();
        matrix.M44 = ReadSingle();

        return matrix;
    }

    public Matrix? ReadMatrixNullable() {
        bool f = ReadBoolean();
        if (f) return ReadMatrix();
        else return null;
    }
}
