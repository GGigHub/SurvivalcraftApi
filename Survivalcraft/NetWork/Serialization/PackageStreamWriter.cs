using System.IO;
using Engine;
using System;
using System.Xml.Linq;
using GameEntitySystem;
using Engine.Graphics;
using Engine.Media;
using System.Collections.Generic;
using TemplatesDatabase;
using Game.NetWork.Packages;

namespace Game.NetWork;

public class PackageStreamWriter : BinaryWriter
{
    public ProjectNet Project;
    public bool IsServer;
    public PackageStreamWriter() : base(new MemoryStream())
    {

    }
    public byte[] Data()
    {
        BaseStream.Seek(0, SeekOrigin.Begin);
        return NetworkManager.Compress(BaseStream);
    }

    public void WriteSmallFloat(float f)
    {
        Write(HalfFloat.FloatToHalf(f));
    }
    public void WriteNullable(float? v)
    {
        Write(v.HasValue);
        if (v.HasValue) Write(v.Value);
    }
    public void WriteNullable(int? v)
    {
        Write(v.HasValue);
        if (v.HasValue) Write(v.Value);
    }

    public virtual void Write(Color color)
    {
        Write(color.R);
        Write(color.G);
        Write(color.B);
        Write(color.A);
    }
    public void Write(Vector4? vector)
    {
        Write(vector.HasValue);
        if (vector.HasValue) Write(vector.Value);
    }

    public void Write(Vector4 vector)
    {
        Write(vector.X);
        Write(vector.Y);
        Write(vector.Z);
        Write(vector.W);
    }

    public void Write(Vector3 vector)
    {
        Write(vector.X);
        Write(vector.Y);
        Write(vector.Z);
    }

    public void WriteSmallVec3(Vector3 vector)
    {
        WriteSmallFloat(vector.X);
        WriteSmallFloat(vector.Y);
        WriteSmallFloat(vector.Z);
    }

    public void Write(Vector3? vector)
    {
        Write(vector.HasValue);
        if (vector.HasValue) Write(vector.Value);
    }
    public void WriteSmallVector3Nuallable(Vector3? vector)
    {
        Write(vector.HasValue);
        if (vector.HasValue) WriteSmallVec3(vector.Value);
    }

    public void Write(Vector2 vector)
    {
        Write(vector.X);
        Write(vector.Y);
    }
    public void WriteSmallVec2(Vector2 vector)
    {
        WriteSmallFloat(vector.X);
        WriteSmallFloat(vector.Y);
    }

    public void Write(Vector2? vector)
    {
        Write(vector.HasValue);
        if (vector.HasValue) Write(vector.Value);
    }

    public virtual void Write(Guid guid)
    {
        byte[] data = guid.ToByteArray();
        Write((byte)data.Length);
        Write(data);
    }
    public void Write(Point3 point)
    {
        Write(point.X);
        Write(point.Y);
        Write(point.Z);
    }
    public void Write(Point2 point)
    {
        Write(point.X);
        Write(point.Y);
    }
    public void Write(Quaternion quaternion)
    {
        Write(quaternion.X);
        Write(quaternion.Y);
        Write(quaternion.Z);
        Write(quaternion.W);
    }
    public void WriteSmallQuaternion(Quaternion quaternion)
    {
        WriteSmallFloat(quaternion.X);
        WriteSmallFloat(quaternion.Y);
        WriteSmallFloat(quaternion.Z);
        WriteSmallFloat(quaternion.W);
    }

    public void Write(Ray3 ray)
    {
        Write(ray.Position);
        Write(ray.Direction);
    }
    public void Write(Ray3? ray)
    {
        Write(ray.HasValue);
        if (ray.HasValue)
        {
            Write(ray.Value);
        }
    }
    public void WriteBlockPoint(Point3 point)
    {
        Write(point.X);
        Write((byte)point.Y);
        Write(point.Z);
    }
    public void Write(CellFace cellFace)
    {
        WriteBlockPoint(cellFace.Point);
        Write((byte)cellFace.Face);
    }
    public void Write(TerrainRaycastResult raycastResult)
    {
        Write(raycastResult.Ray);
        Write(raycastResult.Value);
        Write(raycastResult.CollisionBoxIndex);
        Write(raycastResult.Distance);
        Write(raycastResult.CellFace);
    }
    public void Write(TerrainRaycastResult? raycastResult)
    {
        Write(raycastResult.HasValue);
        if (raycastResult.HasValue)
        {
            Write(raycastResult.Value);
        }
    }
    public void Write(ValuesDictionary vd)
    {
        byte[] data = vd.ToMessagePack();
        Write(data.Length);
        Write(data);
    }

    public void WriteBuff(byte[] buff)
    {
        Write(buff.Length);
        Write(buff);
    }

    public virtual void WriteEnum<T>(T obj) where T : struct
    {
        byte b = Convert.ToByte(obj);
        Write(b);
    }
    public void Write(XElement xElement)
    {
        string str = XmlUtilities.XmlUtils.SaveXmlToString(xElement, true);
        Write(str);
    }

        public void WriteEntityLoadList(List<Entity> entities, Client to)
        {
            ValuesDictionary vd = new ValuesDictionary();
            EntityDataList entityDataList = ((ProjectNet)GameManager.Project).SaveEntitiesAll(entities);
            entityDataList.Save(vd);
            EntityPackage.ConfuseGuid(entities, vd, entityDataList, to);
            WriteBuff(vd.ToMessagePack());
        }

    public void Write(Texture2D texture)
    {
        RenderTarget2D render = new RenderTarget2D(texture.Width, texture.Height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
        var renderTarget = Display.RenderTarget;
        Display.RenderTarget = render;
        Display.Clear(Color.Transparent);
        PrimitivesRenderer2D primitives = new PrimitivesRenderer2D();
        TexturedBatch2D batch2D = primitives.TexturedBatch(texture, true);
        batch2D.QueueQuad(Vector2.Zero, new Vector2(texture.Width, texture.Height), 1f, Vector2.Zero, Vector2.One, Color.White);
        primitives.Flush();
        Display.RenderTarget = renderTarget;
        Image image = new Image(texture.Width, texture.Height);
        render.GetData(image.Pixels, 0, new Rectangle(0, 0, texture.Width, texture.Height));
        Write(image.Width);
        Write(image.Height);
        for (int i = 0; i < image.Pixels.Length; i++) Write(image.Pixels[i]);
    }
    public void Write(Matrix matrix)
    {
        /*            byte[] data = Utilities.StructureToArray(matrix);
                    Write((byte)data.Length);
                    Write(data);*/
        Write(matrix.M11);
        Write(matrix.M21);
        Write(matrix.M31);
        Write(matrix.M41);

        Write(matrix.M12);
        Write(matrix.M22);
        Write(matrix.M32);
        Write(matrix.M42);

        Write(matrix.M13);
        Write(matrix.M23);
        Write(matrix.M33);
        Write(matrix.M43);

        Write(matrix.M14);
        Write(matrix.M24);
        Write(matrix.M34);
        Write(matrix.M44);
    }
    public void Write(Matrix? matrix)
    {
        if (matrix.HasValue)
        {
            Write(true);
            Write(matrix.Value);
        }
        else Write(false);
    }

}

