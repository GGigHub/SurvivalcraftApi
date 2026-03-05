using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

public enum PackageType : byte
{
    ServerInfo,
    ConnectionRequest,
    ConnectionReject,
    Project,
    Client,
    Pickable,
    SubsystemBody,
    SubsystemTerrain,
    SubsystemTime,
    SubsystemSky,
    SubsystemWeather,
    SubsystemElectricity,
    SubsystemPlayers,
    ComponentPlayer,
    ComponentInventory,
    ComponentVitalStat,
    ComponentClothing,
    ComponentBehavior,
    ComponentHealth,
    ComponentMount,
    ComponentSickness,
    ComponentFlu,
    ComponentOnFire,
    ComponentSleep,
    ComponentFurnace,
    PlayerData,
    Message,
    Entity,
    Projectile,
    ReservedTerritoriy,
    Furniture,
    Explosion,
    MovingBlockSet,
    BlockEdit,
    SignBlock,

    Dispenser,
    EditableBlock,
    ReservedGroupManage,
    SubsystemSeason,
    GameBalancer,
    SmallMessage,
    Popup,

    ModsManifest = 250,
    ModsNeed = 251,
    ModsChunk = 252,
    ModsDone = 253,

    ModPackage = 255

}
public class PackageManager
{
    public const byte VerifyByte = 0x88;

    private static IPackage[] basePackages = new IPackage[byte.MaxValue];

    public static void Initialize()
    {
        basePackages = new IPackage[byte.MaxValue];

        var list = typeof(PackageManager).Assembly.GetTypes();
        Type type = typeof(IPackage);
        List<Type> RegList = new List<Type>();
        foreach (var item in list)
        {
            if (type.IsAssignableFrom(item) && !item.IsInterface && !item.IsAbstract)
            {
                RegList.Add(item);
            }
        }
        foreach (var item in RegList)
        {
            var obj = Activator.CreateInstance(item);
            RegisterPackage(obj as IPackage);
        }
    }

    public static void RegisterPackage(IPackage package)
    {
        byte theID = package.ID;
        if (basePackages[theID] == null)
        {
            basePackages[theID] = package;
        }
        else
        {
            throw new Exception("Duplicate package ID.");
        }
    }

    public static void UnRegisterPackage(IPackage package)
    {
        if (basePackages[package.ID] != null)
        {
            basePackages[package.ID] = null;
        }
    }

    public static T DecodePackage<T>(NetNode netNode, NetDataReader reader, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) where T : class
    {
        var obj = DecodePackages(netNode, reader, netPeer, request, iPEndPoint)[0];
        return (T)obj;
    }

    public static List<IPackage> DecodePackagesFromPayload(NetNode netNode, byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null)
    {
        Client from = ResolveFrom(netNode, netPeer, request, iPEndPoint);
        List<IPackage> packages = new List<IPackage>();
        byte packageID = 99;
        if (payload == null || payload.Length == 0)
        {
            return packages;
        }
        try
        {
            var preader = new PackageStreamReader(payload);
            return DecodePackagesFromReader(preader, from, ref packageID);
        }
        catch
        {
            Log.Information("鎺ユ敹鍒颁笉瑙勮寖鐨勬暟鎹寘:" + packageID);
            return packages;
        }
    }

    public static List<IPackage> DecodePackages(NetNode netNode, NetDataReader reader, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null)
    {
        Client from = ResolveFrom(netNode, netPeer, request, iPEndPoint);
        List<IPackage> packages = new List<IPackage>();
        byte packageID = 99;
        if (reader.AvailableBytes == 0)
        {
            return packages;
        }
        try
        {
            var preader = CommonLib.GetReader(reader);
            return DecodePackagesFromReader(preader, from, ref packageID);
        }
        catch
        {
            Log.Information("鎺ユ敹鍒颁笉瑙勮寖鐨勬暟鎹寘:" + packageID);
            return packages;
        }
    }

    private static Client ResolveFrom(NetNode netNode, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null)
    {
        Client from = null;
        if (netNode != null)
        {
            from = netNode.Clients.Values.Where(client =>
                (client.Peer != null && client.Peer == netPeer) ||
                (client.Request != null && client.Request == request) ||
                (client.IPPoint != null && client.IPPoint == iPEndPoint)).FirstOrDefault();
        }
        if (from == null)
        {
            if (netPeer != null) from = new Client(netPeer);
            if (request != null) from = new Client(request);
            if (iPEndPoint != null) from = new Client(iPEndPoint);
        }
        return from;
    }

    private static List<IPackage> DecodePackagesFromReader(PackageStreamReader preader, Client from, ref byte packageID)
    {
        List<IPackage> packages = new List<IPackage>();
        IPackage last = null;
        while (preader.BaseStream.Position < preader.BaseStream.Length)
        {
            byte check = preader.ReadByte();
            if (check != VerifyByte)
            {
                Log.Information("Packet verify failed, last packet: " + (last == null ? "null" : last.GetType().Name));
                break;
            }
            byte ID = preader.ReadByte();
            packageID = ID;
            if (basePackages[ID] != null)
            {
                object obj = Activator.CreateInstance(basePackages[ID].GetType());
                IPackage package = (IPackage)obj;
                last = package;
                package.From = from;
                package.ReadData(preader);
                packages.Add(package);
            }
            else
            {
                Log.Information(string.Format($"涓嶅瓨鍦ㄧ殑鍖匢D:{ID}"));
                break;
            }
        }
        return packages;
    }
}

