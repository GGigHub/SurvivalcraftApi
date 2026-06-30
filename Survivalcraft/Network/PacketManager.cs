using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public abstract class Packet {
    public abstract byte ID { get; }
    public abstract void Handle(bool isServer);
    public abstract void Serialize(PacketSerializer archive);

    public ClientSession From { get; set; } = null;
    public ClientSession To { get; set; } = null;
    public ClientSession Except { get; set; } = null;
    public virtual NetworkState MinNeedState => NetworkState.NotConnected;
}

public enum PacketType : byte {
    // Connect
    ConnectInfo = 0, IndexInfo = 1, ProjectInfo = 2, StateInfo = 3,

    // Entity
    EntityRemove = 25, EntityLoad = 26, EntitySyncRequest = 27,
    BodyUpdate = 33, ApplyImpulse = 34, BodyCollision = 35,
    BehaviorLookAround = 54, BehaviorStubbornSteed = 55,
    BehaviorChase = 56, BehaviorRandomPeck = 57, BehaviorRandomFeed = 58, BehaviorEatPickable = 59,
    BehaviorHumanRow = 60, BehaviorCreatureSound = 61, BehaviorDigInMud = 62, BehaviorFishOutOfWater = 63,
    EntityOnFire = 68,

    // Health
    HealthSync = 28, InjureEvent = 29, InjureRequest = 30, HitResult = 31, DamageSync = 32,
    VitalStatSync = 83,

    // Inventory
    Furnace = 12,
    Dispenser = 13,
    ActiveSlotChange = 36, InventorySync = 37, MoveItem = 38, DragDropItem = 39, SetSlotItem = 40,
    ClothingRequestSkin = 64, ClothingReplySkin = 65, ClothingWhoHas = 66, ClothingWhoHasReply = 67,
    PickableCreate = 93, PickableUpdate = 94, PickableDelete = 95, PickableRequestSync = 96,
    PickableSetFlyToPosition = 97, PickableCreateList = 98, PickableDeleteList = 99, PickableSyncList = 100,

    // Logic
    Electricity = 7,
    SignBlock = 109,
    EditSign = 18,
    EditableMemoryBank = 84, EditableTruthTable = 85, EditableAdjustableDelayGate = 86,
    EditableBattery = 87, EditablePiston = 88, EditableSwitch = 89, EditableButton = 90,
    MovingBlockAdd = 91, MovingBlockRemove = 92,

    // Mechanics
    Mount = 8,
    Sickness = 9, Flu = 10, Projectile = 11,
    OpenInventoryByID = 15, OpenInventoryByPoint = 16, CrossbowPull = 17,
    SleepRequest = 19, SleepStatus = 20, WakeupRequest = 21, WakeupStatus = 22,
    ExplosionSound = 23, ExplosionCell = 24,
    FireBlockAdd = 69, FireBlockRemove = 70,

    // Player
    PlayerCreate = 41, PlayerModify = 42, PlayerDelete = 43, PlayerAdd = 44, PlayerKnown = 45,
    PlayerUpdateLocation = 46, ServerCloseTime = 47, PlayerBugle = 48, PlayerCount = 49,
    PlayerNoMsgAdd = 50, PlayerNoMsgRemove = 51,
    PlayerBodyUpdate = 71, PlayerInteract = 72, PlayerAim = 73, PlayerDig = 74, PlayerHit = 75,
    PlayerCreativeFly = 76, PlayerIntoPlaying = 77, PlayerRestart = 78, PlayerAddExperience = 79,
    PlayerDragDrop = 80, PlayerStatSync = 81, PlayerPositionSet = 82,

    // Terrain
    Time = 4, Sky = 5,
    Weather = 6, Season = 14,
    TerrainRequestSyncChunks = 101, TerrainSyncChunkList = 102, TerrainReplyResult = 103,
    TerrainChangeCell = 104, TerrainRequestChangeCell = 105, TerrainChangeCellList = 106,

    ModPacket = 255
}

public class PacketManager {
    public const byte VerifyByte = 0x88;
    public const byte RelayVerifyByte = 0x89;

    private static readonly Func<Packet>[] m_factories = new Func<Packet>[256];

    public static void Initialize() {
        Array.Clear(m_factories, 0, m_factories.Length);

        // Connect
        RegisterPacket<ConnectPacket>();
        RegisterPacket<IndexPacket>();
        RegisterPacket<ProjectPacket>();
        RegisterPacket<StatePacket>();

        // Entity
        RegisterPacket<BodyCollisionPacket>();
        RegisterPacket<BodyUpdatePacket>();
        RegisterPacket<ChaseBehaviorPacket>();
        RegisterPacket<CreatureSoundPacket>();
        RegisterPacket<DigInMudBehaviorPacket>();
        RegisterPacket<EatPickableBehaviorPacket>();
        RegisterPacket<EntityLoadPacket>();
        RegisterPacket<EntityRemovePacket>();
        RegisterPacket<EntitySyncRequestPacket>();
        RegisterPacket<FishOutOfWaterBehaviorPacket>();
        RegisterPacket<HumanRowBehaviorPacket>();
        RegisterPacket<LookAroundBehaviorPacket>();
        RegisterPacket<RandomFeedBehaviorPacket>();
        RegisterPacket<RandomPeckBehaviorPacket>();
        RegisterPacket<StubbornSteedBehaviorPacket>();

        // Health
        RegisterPacket<HealthSyncPacket>();
        RegisterPacket<HitValuePacket>();
        RegisterPacket<InjureEventPacket>();
        RegisterPacket<InjureRequestPacket>();
        RegisterPacket<VitalStatSyncPacket>();

        // Inventory
        RegisterPacket<ActiveSlotChangePacket>();
        RegisterPacket<ComponentFurnacePacket>();
        RegisterPacket<DispenserPacket>();
        RegisterPacket<DragDropItemPacket>();
        RegisterPacket<InventorySyncPacket>();
        RegisterPacket<MoveItemPacket>();
        RegisterPacket<OpenInventoryByIDPacket>();
        RegisterPacket<OpenInventoryByPointPacket>();
        RegisterPacket<PickableCreateListPacket>();
        RegisterPacket<PickableCreatePacket>();
        RegisterPacket<PickableDeleteListPacket>();
        RegisterPacket<PickableDeletePacket>();
        RegisterPacket<PickableRequestSyncPacket>();
        RegisterPacket<PickableSetFlyToPositionPacket>();
        RegisterPacket<PickableSyncListPacket>();
        RegisterPacket<PickableUpdatePacket>();
        RegisterPacket<ReplySkinPacket>();
        RegisterPacket<RequestSkinPacket>();
        RegisterPacket<SetSlotItemPacket>();
        RegisterPacket<WhoHasSkinPacket>();
        RegisterPacket<WhoHasSkinReplyPacket>();
        RegisterPacket<DamageSyncPacket>();

        // Logic
        RegisterPacket<AdjustableDelayGatePacket>();
        RegisterPacket<BatteryVoltagePacket>();
        RegisterPacket<ButtonVoltagePacket>();
        RegisterPacket<EditSignPacket>();
        RegisterPacket<MemoryBankDataPacket>();
        RegisterPacket<MovingBlockAddPacket>();
        RegisterPacket<MovingBlockRemovePacket>();
        RegisterPacket<PistonDataPacket>();
        RegisterPacket<SignBlockPacket>();
        RegisterPacket<SubsystemElectricityPacket>();
        RegisterPacket<SwitchVoltagePacket>();
        RegisterPacket<TruthTableDataPacket>();

        // Mechanics
        RegisterPacket<ComponentFluPacket>();
        RegisterPacket<ComponentMountPacket>();
        RegisterPacket<ComponentSicknessPacket>();
        RegisterPacket<CrossbowPullPacket>();
        RegisterPacket<EntityOnFirePacket>();
        RegisterPacket<ExplosionCellPacket>();
        RegisterPacket<ExplosionSoundPacket>();
        RegisterPacket<FireBlockAddPacket>();
        RegisterPacket<FireBlockRemovePacket>();
        RegisterPacket<ProjectilePacket>();
        RegisterPacket<SleepRequestPacket>();
        RegisterPacket<SleepStatusPacket>();
        RegisterPacket<WakeupRequestPacket>();
        RegisterPacket<WakeupStatusPacket>();

        // Player
        RegisterPacket<PlayerAddExperiencePacket>();
        RegisterPacket<PlayerAddPacket>();
        RegisterPacket<PlayerAimPacket>();
        RegisterPacket<PlayerBodyUpdatePacket>();
        RegisterPacket<PlayerBuglePacket>();
        RegisterPacket<PlayerCountPacket>();
        RegisterPacket<PlayerCreatePacket>();
        RegisterPacket<PlayerCreativeFlyPacket>();
        RegisterPacket<PlayerDeletePacket>();
        RegisterPacket<PlayerDigPacket>();
        RegisterPacket<PlayerDragDropPacket>();
        RegisterPacket<PlayerHitPacket>();
        RegisterPacket<PlayerInteractPacket>();
        RegisterPacket<PlayerIntoPlayingPacket>();
        RegisterPacket<PlayerKnownPacket>();
        RegisterPacket<PlayerModifyPacket>();
        RegisterPacket<PlayerNoMsgAddPacket>();
        RegisterPacket<PlayerNoMsgRemovePacket>();
        RegisterPacket<PlayerPositionSetPacket>();
        RegisterPacket<PlayerRestartPacket>();
        RegisterPacket<PlayerStatSyncPacket>();
        RegisterPacket<PlayerUpdateLocationPacket>();
        RegisterPacket<ServerCloseTimePacket>();

        // Terrain
        RegisterPacket<SubsystemSeasonPacket>();
        RegisterPacket<SubsystemSkyPacket>();
        RegisterPacket<SubsystemTimePacket>();
        RegisterPacket<SubsystemWeatherPacket>();
        RegisterPacket<TerrainChangeCellListPacket>();
        RegisterPacket<TerrainChangeCellPacket>();
        RegisterPacket<TerrainReplyResultPacket>();
        RegisterPacket<TerrainRequestChangeCellPacket>();
        RegisterPacket<TerrainRequestSyncChunksPacket>();
        RegisterPacket<TerrainSyncChunkListPacket>();
    }

    public static void RegisterPacket<T>() where T : Packet, new() {
        var instance = new T();
        byte id = instance.ID;
        if (m_factories[id] != null) {
            throw new Exception($"Duplicate packet ID: {id}");
        }
        m_factories[id] = () => new T();
    }

    public static void RegisterPacket(Packet packet) {
        byte id = packet.ID;
        if (m_factories[id] != null) {
            throw new Exception($"Duplicate packet ID: {id}");
        }
        // This is for dynamic registration (e.g. from mods)
        // Since we can't easily get a generic factory here without reflection,
        // we store the type and use Activator only for these cases, or better, 
        // encourage mods to use RegisterPacket<T>().
        // For simplicity in this transition, we'll use a capture if it's already an instance.
        m_factories[id] = () => (Packet)Activator.CreateInstance(packet.GetType());
    }

    public static void UnRegisterPacket(byte id) {
        m_factories[id] = null;
    }

    public static void UnRegisterPacket(Packet packet) {
        UnRegisterPacket(packet.ID);
    }

    public static List<Packet> DecodePackets(byte[] payload) {
        List<Packet> packets = [];
        byte packetID = 0;
        if (payload == null || payload.Length == 0) {
            return packets;
        }
        try {
            PacketStreamReader preader = new(payload);
            return DecodePacketsFromReader(preader, ref packetID);
        }
        catch {
            Log.Information("接收到不规范的数据包:" + packetID);
            return packets;
        }
    }

    private static List<Packet> DecodePacketsFromReader(PacketStreamReader preader, ref byte packetID, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null) {
        List<Packet> packets = [];
        Packet last = null;
        ClientSession from = null;

        while (preader.BaseStream.Position < preader.BaseStream.Length) {
            byte check = preader.ReadByte();
            if (check != VerifyByte && check != RelayVerifyByte) {
                Log.Information("Packet verify failed, last packet: " + (last == null ? "null" : last.GetType().Name));
                break;
            }
            byte ID = preader.ReadByte();
            packetID = ID;

            int senderIndex = from?.PlayerIndex ?? -1;
            if (check == RelayVerifyByte) {
                int count = preader.ReadInt32();
                for (int i = 0; i < count; i++) {
                    int index = preader.ReadInt32();
                    // 假设中继情况中只有一个sender
                    if (i == 0) {
                        senderIndex = index;
                    }
                }
            }

            if (m_factories[ID] != null) {
                Packet packet = m_factories[ID]();
                last = packet;

                if (check == RelayVerifyByte && from == null) {
                    packet.From = Enumerable.FirstOrDefault(
                        NetworkManager.NetworkServer?.ClientSessions.Values, c => c.PlayerIndex == senderIndex);
                }
                else {
                    packet.From = from;
                }

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
