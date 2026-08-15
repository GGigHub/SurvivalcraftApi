using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public abstract class Packet {
    public abstract byte ID { get; }
    public abstract void Handle(bool isServer);
    public abstract void Serialize(PacketSerializer archive);

    public virtual byte Channel => NetworkChannels.Default;

    public ClientSession From { get; set; } = null;
    public ClientSession To { get; set; } = null;
    public ClientSession Except { get; set; } = null;
    public virtual NetworkState MinNeedState => NetworkState.NotConnected;
}

public enum PacketType : byte {
    ConnectInfo,
    IndexInfo,
    ProjectInfo,
    StateInfo,
    Time,
    Sky,
    Weather,
    Electricity,
    Mount,
    Sickness,
    Flu,
    Projectile,
    Furnace,
    Dispenser,
    OpenInventoryByID,
    OpenInventoryByPoint,
    CrossbowPull,
    EditSign,
    SleepRequest,
    SleepStatus,
    WakeupRequest,
    WakeupStatus,
    Explosion,
    EntityRemove,
    EntityLoad,
    EntitySyncRequest,
    HealthSync,
    InjureEvent,
    InjureRequest,
    HitResult,
    DamageSync,
    BodyCollision,
    ActiveSlotChange,
    InventorySync,
    MoveItem,
    DragDropItem,
    SetSlotItem,
    PlayerCreate,
    PlayerModify,
    PlayerDelete,
    PlayerAdd,
    PlayerKnown,
    Chat,
    PlayerBugle,
    PlayerNoMsgAdd,
    PlayerNoMsgRemove,
    ClothingRequestSkin,
    ClothingReplySkin,
    ClothingWhoHas,
    ClothingWhoHasReply,
    EntityOnFire,
    FireBlockAdd,
    FireBlockRemove,
    PlayerBodyUpdate,
    PlayerInteract,
    PlayerAim,
    PlayerDig,
    PlayerHit,
    PlayerCreativeFly,
    PlayerIntoPlaying,
    PlayerRestart,
    PlayerAddExperience,
    PlayerDragDrop,
    PlayerStatSync,
    PlayerPositionSet,
    VitalStatSync,
    EditableMemoryBank,
    EditableTruthTable,
    EditableAdjustableDelayGate,
    EditableBattery,
    EditablePiston,
    EditableSwitch,
    EditableButton,
    MovingBlockAdd,
    MovingBlockRemove,
    PickableCreate,
    PickableUpdate,
    PickableDelete,
    PickableRequestSync,
    PickableSetFlyToPosition,
    PickableCreateList,
    PickableDeleteList,
    PickableSyncList,
    TerrainSyncChunkList,
    TerrainViewDistance,
    TerrainChangeCell,
    TerrainChangeCellList,
    PlayerDrop,
    FurnitureHammer,
    SignBlock,
    ClothingSync,
    LatencyProbe,
    PlayerDigRequest,
    PlayerDigConfirmation,
    PlayerPlaceConfirmation,
    EntityBodyUpdate,
    DisplayMessage,
    PlayerPickBlock,
    ModPacket
}

public static class PacketManager {
    public static PacketFragment PackageFragment { get; } = new();
    private const byte VerifyByte = 0x88;

    private static readonly Func<Packet>[] m_factories = new Func<Packet>[256];

    public static void Initialize() {
        Array.Clear(m_factories, 0, m_factories.Length);

        // Connect
        RegisterPacket<ConnectPacket>();
        RegisterPacket<IndexPacket>();
        RegisterPacket<LatencyProbePacket>();
        RegisterPacket<ProjectPacket>();
        RegisterPacket<StatePacket>();

        // Entity
        RegisterPacket<BodyCollisionPacket>();
        RegisterPacket<EntityLoadPacket>();
        RegisterPacket<EntityRemovePacket>();
        RegisterPacket<EntitySyncRequestPacket>();
        RegisterPacket<EntityBodyUpdatePacket>();

        // Health
        RegisterPacket<HealthSyncPacket>();
        RegisterPacket<InjureEventPacket>();
        RegisterPacket<InjureRequestPacket>();
        RegisterPacket<VitalStatSyncPacket>();
        RegisterPacket<DamageSyncPacket>();

        // Inventory
        RegisterPacket<ActiveSlotChangePacket>();
        RegisterPacket<ComponentFurnacePacket>();
        RegisterPacket<DisplayMessagePacket>();
        RegisterPacket<DispenserPacket>();
        RegisterPacket<DragDropItemPacket>();
        RegisterPacket<ClothingSyncPacket>();
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
        RegisterPacket<FurnitureHammerPacket>();
        RegisterPacket<ReplySkinPacket>();
        RegisterPacket<RequestSkinPacket>();
        RegisterPacket<SetSlotItemPacket>();
        RegisterPacket<WhoHasSkinPacket>();
        RegisterPacket<WhoHasSkinReplyPacket>();

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
        RegisterPacket<ExplosionPacket>();
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
        RegisterPacket<ChatMessagePacket>();
        RegisterPacket<PlayerBodyUpdatePacket>();
        RegisterPacket<PlayerBuglePacket>();
        RegisterPacket<PlayerCreatePacket>();
        RegisterPacket<PlayerCreativeFlyPacket>();
        RegisterPacket<PlayerDeletePacket>();
        RegisterPacket<PlayerDigPacket>();
        RegisterPacket<PlayerDigRequestPacket>();
        RegisterPacket<PlayerDigConfirmationPacket>();
        RegisterPacket<PlayerPlaceConfirmationPacket>();
        RegisterPacket<PlayerDropPacket>();
        RegisterPacket<PlayerDragDropPacket>();
        RegisterPacket<PlayerHitPacket>();
        RegisterPacket<PlayerInteractPacket>();
        RegisterPacket<PlayerPickBlockPacket>();
        RegisterPacket<PlayerIntoPlayingPacket>();
        RegisterPacket<PlayerKnownPacket>();
        RegisterPacket<PlayerModifyPacket>();
        RegisterPacket<PlayerNoMsgAddPacket>();
        RegisterPacket<PlayerNoMsgRemovePacket>();
        RegisterPacket<PlayerPositionSetPacket>();
        RegisterPacket<PlayerRestartPacket>();
        RegisterPacket<PlayerStatSyncPacket>();
        RegisterPacket<HitValuePacket>();

        // Terrain
        RegisterPacket<SubsystemSkyPacket>();
        RegisterPacket<SubsystemTimePacket>();
        RegisterPacket<SubsystemWeatherPacket>();
        RegisterPacket<TerrainChangeCellListPacket>();
        RegisterPacket<TerrainChangeCellPacket>();
        RegisterPacket<TerrainSyncChunkListPacket>();
        RegisterPacket<TerrainViewDistancePacket>();
    }

    public static void RegisterPacket<T>() where T : Packet, new() {
        var instance = new T();
        byte id = instance.ID;
        if (m_factories[id] != null) {
            throw new Exception($"Duplicate packet ID: {id}");
        }
        m_factories[id] = () => new T();
    }

    public static void UnRegisterPacket(byte id) {
        m_factories[id] = null;
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
            if (check != VerifyByte) {
                Log.Information("Packet verify failed, last packet: " + (last == null ? "null" : last.GetType().Name));
                break;
            }
            byte ID = preader.ReadByte();
            packetID = ID;

            if (m_factories[ID] != null) {
                Packet packet = m_factories[ID]();
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
