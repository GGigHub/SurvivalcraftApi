using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public abstract class Packet {
    public abstract byte ID { get; }
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public abstract ClientStage MinNeedState { get; }

    public abstract void Handle(bool isServer);
    public abstract void Serialize(PacketSerializer archive);
}

public enum PacketType : byte {
    ServerInfo, ConnectRequest, ConnectReject,
    Project, Time, Sky,
    Weather, Electricity, Mount,
    Sickness, Flu, Furnace, Projectile,
    SignBlock, Dispenser, Season,

    OpenInventoryByID, OpenInventoryByPoint, CrossbowPull, EditSign,
    SleepRequest, SleepStatus, WakeupRequest, WakeupStatus,
    ExplosionSound, ExplosionCell,
    EntityRemove, EntityLoad, EntitySyncRequest,
    HealthSync, InjureEvent, InjureRequest, HitResult, DamageSync,
    BodyUpdate, ApplyImpulse, BodyCollision,
    ActiveSlotChange, InventorySync, MoveItem, DragDropItem, SetSlotItem,
    PlayerCreate, PlayerModify, PlayerDelete, PlayerAdd, PlayerKnown,
    PlayerUpdateLocation, ServerCloseTime, PlayerBugle, PlayerCount,
    PlayerNoMsgAdd, PlayerNoMsgRemove,
    ClientAdd, ClientRemove, ClientSyncList, ClientStateChange,
    BehaviorChase, BehaviorRandomPeck, BehaviorRandomFeed, BehaviorEatPickable,
    BehaviorHumanRow, BehaviorCreatureSound, BehaviorDigInMud, BehaviorFishOutOfWater,
    ClothingRequestSkin, ClothingReplySkin, ClothingWhoHas, ClothingWhoHasReply,
    EntityOnFire, FireBlockAdd, FireBlockRemove,
    PlayerBodyUpdate, PlayerInteract, PlayerAim, PlayerDig, PlayerHit,
    PlayerCreativeFly, PlayerIntoPlaying, PlayerRestart, PlayerAddExperience,
    PlayerDragDrop, PlayerStatSync, PlayerPositionSet,
    VitalStatSync,
    EditableMemoryBank, EditableTruthTable, EditableAdjustableDelayGate,
    EditableBattery, EditablePiston, EditableSwitch, EditableButton,
    MovingBlockAdd, MovingBlockRemove,
    PickableCreate, PickableUpdate, PickableDelete, PickableRequestSync,
    PickableSetFlyToPosition, PickableCreateList, PickableDeleteList, PickableSyncList,
    TerrainRequestSyncChunks, TerrainSyncChunkList, TerrainReplyResult,
    TerrainChangeCell, TerrainRequestChangeCell, TerrainChangeCellList,

    ModPacket = 255
}

public class PacketManager {
    public const byte VerifyByte = 0x88;

    private static Packet[] basePackets = new Packet[byte.MaxValue];

    public static void Initialize() {
        basePackets = new Packet[byte.MaxValue];

        var list = typeof(PacketManager).Assembly.GetTypes();
        Type type = typeof(Packet);
        List<Type> RegList = new List<Type>();
        foreach (var item in list) {
            if (type.IsAssignableFrom(item) && !item.IsInterface && !item.IsAbstract) {
                RegList.Add(item);
            }
        }
        foreach (var item in RegList) {
            var obj = Activator.CreateInstance(item);
            RegisterPacket(obj as Packet);
        }
    }

    public static void RegisterPacket(Packet packet) {
        byte theID = packet.ID;
        if (basePackets[theID] == null) {
            basePackets[theID] = packet;
        }
        else {
            throw new Exception("Duplicate packet ID.");
        }
    }

    public static void UnRegisterPacket(Packet packet) {
        if (basePackets[packet.ID] != null) {
            basePackets[packet.ID] = null;
        }
    }

    public static T DecodePacket<T>(INetworkNode networkBase, NetDataReader reader, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) where T : Packet {
        var obj = DecodePackets(networkBase, reader, netPeer, request, iPEndPoint)[0];
        return (T)obj;
    }

    public static List<Packet> DecodePacketsFromPayload(INetworkNode networkBase, byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) {
        Client from = ResolveFrom(networkBase, netPeer, request, iPEndPoint);
        List<Packet> packets = new List<Packet>();
        byte packetID = 99;
        if (payload == null || payload.Length == 0) {
            return packets;
        }
        try {
            var preader = new PacketStreamReader(payload);
            return DecodePacketsFromReader(preader, from, ref packetID);
        }
        catch {
            Log.Information("接收到不规范的数据包:" + packetID);
            return packets;
        }
    }

    public static List<Packet> DecodePackets(INetworkNode networkBase, NetDataReader reader, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) {
        Client from = ResolveFrom(networkBase, netPeer, request, iPEndPoint);
        List<Packet> packets = new List<Packet>();
        byte packetID = 99;
        if (reader.AvailableBytes == 0) {
            return packets;
        }
        try {
            var preader = NetworkManager.GetReader(reader);
            return DecodePacketsFromReader(preader, from, ref packetID);
        }
        catch {
            Log.Information("接收到不规范的数据包:" + packetID);
            return packets;
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

    private static List<Packet> DecodePacketsFromReader(PacketStreamReader preader, Client from, ref byte packetID) {
        List<Packet> packets = new List<Packet>();
        Packet last = null;
        while (preader.BaseStream.Position < preader.BaseStream.Length) {
            byte check = preader.ReadByte();
            if (check != VerifyByte) {
                Log.Information("Packet verify failed, last packet: " + (last == null ? "null" : last.GetType().Name));
                break;
            }
            byte ID = preader.ReadByte();
            packetID = ID;
            if (basePackets[ID] != null) {
                object obj = Activator.CreateInstance(basePackets[ID].GetType());
                Packet packet = (Packet)obj;
                last = packet;
                packet.From = from;
                packet.Serialize(new PacketSerializer(preader));
                packets.Add(packet);
            }
            else {
                Log.Information(string.Format($"不存在的包ID:{ID}"));
                break;
            }
        }
        return packets;
    }
}


