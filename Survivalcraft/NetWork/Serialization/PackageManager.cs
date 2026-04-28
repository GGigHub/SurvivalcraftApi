using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

public abstract class IPackage {
    public abstract byte ID { get; }
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public abstract ClientStage MinNeedState { get; }

    public abstract void Handle(bool isServer);
    public abstract void Serialize(PackageSerializer archive);
}

public enum PackageType : byte {
    ServerInfo, ConnectRequest, ConnectReject,
    Client, Project, PlayerData, Terrain, Entity,
    Pickable, Body, Time, Sky,
    Weather, Electricity, Player,
    Inventory, VitalStat, Clothing,
    Behavior, Health, Mount,
    Sickness, Flu, OnFire,
    Sleep, Furnace, Projectile,
    ReservedTerritoriy, Explosion, MovingBlockSet,
    BlockEdit, SignBlock, Dispenser,
    EditableBlock, Season, SmallMessage, Popup,

    ModPackage = 255
}

public class PackageManager {
    public const byte VerifyByte = 0x88;

    private static IPackage[] basePackages = new IPackage[byte.MaxValue];

    public static void Initialize() {
        basePackages = new IPackage[byte.MaxValue];

        var list = typeof(PackageManager).Assembly.GetTypes();
        Type type = typeof(IPackage);
        List<Type> RegList = new List<Type>();
        foreach (var item in list) {
            if (type.IsAssignableFrom(item) && !item.IsInterface && !item.IsAbstract) {
                RegList.Add(item);
            }
        }
        foreach (var item in RegList) {
            var obj = Activator.CreateInstance(item);
            RegisterPackage(obj as IPackage);
        }
    }

    public static void RegisterPackage(IPackage package) {
        byte theID = package.ID;
        if (basePackages[theID] == null) {
            basePackages[theID] = package;
        }
        else {
            throw new Exception("Duplicate package ID.");
        }
    }

    public static void UnRegisterPackage(IPackage package) {
        if (basePackages[package.ID] != null) {
            basePackages[package.ID] = null;
        }
    }

    public static T DecodePackage<T>(INetworkNode networkBase, NetDataReader reader, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) where T : IPackage {
        var obj = DecodePackages(networkBase, reader, netPeer, request, iPEndPoint)[0];
        return (T)obj;
    }

    public static List<IPackage> DecodePackagesFromPayload(INetworkNode networkBase, byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) {
        Client from = ResolveFrom(networkBase, netPeer, request, iPEndPoint);
        List<IPackage> packages = new List<IPackage>();
        byte packageID = 99;
        if (payload == null || payload.Length == 0) {
            return packages;
        }
        try {
            var preader = new PackageStreamReader(payload);
            return DecodePackagesFromReader(preader, from, ref packageID);
        }
        catch {
            Log.Information("接收到不规范的数据包:" + packageID);
            return packages;
        }
    }

    public static List<IPackage> DecodePackages(INetworkNode networkBase, NetDataReader reader, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) {
        Client from = ResolveFrom(networkBase, netPeer, request, iPEndPoint);
        List<IPackage> packages = new List<IPackage>();
        byte packageID = 99;
        if (reader.AvailableBytes == 0) {
            return packages;
        }
        try {
            var preader = NetworkManager.GetReader(reader);
            return DecodePackagesFromReader(preader, from, ref packageID);
        }
        catch {
            Log.Information("接收到不规范的数据包:" + packageID);
            return packages;
        }
    }

    private static Client ResolveFrom(INetworkNode networkBase, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) {
        Client from = null;
        if (networkBase != null) {
            from = networkBase.Clients.Values.Where(client =>
                (client.Peer != null && client.Peer == netPeer) ||
                (client.Request != null && client.Request == request) ||
                (client.IPPoint != null && client.IPPoint == iPEndPoint)).FirstOrDefault();
        }
        if (from == null) {
            if (netPeer != null) from = new Client(netPeer);
            if (request != null) from = new Client(request);
            if (iPEndPoint != null) from = new Client(iPEndPoint);
        }
        return from;
    }

    private static List<IPackage> DecodePackagesFromReader(PackageStreamReader preader, Client from, ref byte packageID) {
        List<IPackage> packages = new List<IPackage>();
        IPackage last = null;
        while (preader.BaseStream.Position < preader.BaseStream.Length) {
            byte check = preader.ReadByte();
            if (check != VerifyByte) {
                Log.Information("Packet verify failed, last packet: " + (last == null ? "null" : last.GetType().Name));
                break;
            }
            byte ID = preader.ReadByte();
            packageID = ID;
            if (basePackages[ID] != null) {
                object obj = Activator.CreateInstance(basePackages[ID].GetType());
                IPackage package = (IPackage)obj;
                last = package;
                package.From = from;
                package.Serialize(new PackageSerializer(preader));
                packages.Add(package);
            }
            else {
                Log.Information(string.Format($"不存在的包ID:{ID}"));
                break;
            }
        }
        return packages;
    }
}

