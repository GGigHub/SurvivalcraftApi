using Engine;
using System;
using System.Collections.Generic;

namespace Game.NetWork.Packages;

public class ComponentPlayerPackage : IPackage
{

    public byte ID => (byte)PackageType.ComponentPlayer;
    public enum PlayerAction
    {
        BodyUpdate,
        InteractEvent,
        AimEvent,
        DigEvent,
        Hit,
        CreativeFlyChange,
        IntoPlaying,
        Restart,
        AddExperience,
        Drop,
        DragDrop,
        SyncStat,
        PositionSet
    }
    public enum ChangFlag : byte
    {
        None = 0,
        ParentBodyChange = 1,
        LookAnglesChange = 2,
        LadderChange = 4,
        PositionChange = 8,
        RotationChange = 16,
        VelocityChange = 32,
        ChildLookAnglesChange = 64,
        SneakChange = 128
    }
    public bool NeedHandleMainPlayer = true;

    // Server-side melee rate limiting (per-player)
    private static readonly Dictionary<Guid, double> s_lastHitTimes = [];
    private const double SERVER_HIT_COOLDOWN = 0.2;

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public PlayerData m_playerData;
    public PlayerAction m_type;

    public int m_ActiveSlot;

    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Rotation;
    public Vector2 LookOrder;
    public Vector2 LookAngles;
    public Vector2 ChildLookAngles;
    public int m_inventory_id;
    public ChangFlag changFlag;

    public byte m_fromPlayerId;
    public ushort bodyId;
    public Vector3 m_hitPosition;
    public Vector3 m_hitDirection;
    public int m_count;

    public ComponentPlayer.InteractEvent m_interactEvent;
    public ComponentPlayer.AimEvent m_aimEvent;
    public ComponentPlayer.DigEvent m_digEvent;

    public TerrainRaycastResult? m_netInteractRaycast;
    public Ray3 m_netInteractRay;
    public Ray3? m_netAimRay;
    public TerrainRaycastResult? m_netDigRaycast;
    public Ray3? m_netDigRay;
    public int m_ladderValue;
    public bool m_sneaking;
    public float m_level;
    public bool m_isCreativeFly;
    public bool m_playSound;
    public TemplatesDatabase.ValuesDictionary m_stat;

    public ComponentPlayerPackage()
    {

    }
    public ComponentPlayerPackage(ComponentPlayer player, PlayerAction type)
    {
        m_type = type;
        m_playerData = player.PlayerData;
        switch (type)
        {
            case PlayerAction.BodyUpdate:
                var body = player.ComponentBody.ParentBody;
                if (body != null)
                {
                    changFlag |= ChangFlag.ParentBodyChange;
                    if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue)
                    {
                        changFlag |= ChangFlag.LookAnglesChange;
                        LookAngles = body.Locomotion.SendLookAngles.Value;
                        body.Locomotion.SendLookAngles = null;
                    }
                    if (player.ComponentLocomotion.SendLookAngles.HasValue)
                    {
                        changFlag |= ChangFlag.ChildLookAnglesChange;
                        ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                        player.ComponentLocomotion.SendLookAngles = null;
                    }
                }
                else
                {
                    body = player.ComponentBody;
                    if (player.ComponentLocomotion.SendLookAngles.HasValue)
                    {
                        changFlag |= ChangFlag.LookAnglesChange;
                        LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                        player.ComponentLocomotion.SendLookAngles = null;
                    }
                    if (player.ComponentLocomotion.LadderValue.HasValue)
                    {
                        changFlag |= ChangFlag.LadderChange;
                        m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
                    }
                    if (body.CanCrouch)
                    {
                        changFlag |= ChangFlag.SneakChange;
                        m_sneaking = body.IsCrouching;
                    }
                }
                if (body.SendPosition.HasValue)
                {
                    Position = body.SendPosition.Value;
                    changFlag |= ChangFlag.PositionChange;
                    body.SendPosition = null;
                }
                if (body.SendRotation.HasValue)
                {
                    changFlag |= ChangFlag.RotationChange;
                    Rotation = body.SendRotation.Value;
                    body.SendRotation = null;
                }
                if (body.SendVelocity.HasValue)
                {
                    changFlag |= ChangFlag.VelocityChange;
                    Velocity = body.SendVelocity.Value;
                    body.SendVelocity = null;
                }
                break;
            case PlayerAction.DigEvent:
                m_digEvent = player.digEvent.digEvent;
                m_netDigRay = player.digEvent.NetDigRay;
                m_netDigRaycast = player.digEvent.NetDigRaycast;
                break;
            case PlayerAction.AimEvent:
                m_aimEvent = player.aimEvent.aimEvent;
                m_netAimRay = player.aimEvent.NetAim;
                break;
            case PlayerAction.InteractEvent:
                m_interactEvent = player.interactEvent.interactEvent;
                m_netInteractRay = player.interactEvent.NetInteractRay;
                m_netInteractRaycast = player.interactEvent.NetPlaceRaycast;
                break;
            case PlayerAction.CreativeFlyChange:
                m_isCreativeFly = player.ComponentLocomotion.IsCreativeFlyEnabled;
                break;
            case PlayerAction.SyncStat:
                m_stat = new TemplatesDatabase.ValuesDictionary();
                player.PlayerStats.Save(m_stat);
                break;
            case PlayerAction.PositionSet:
                Position = player.ComponentBody.Position;
                Velocity = player.ComponentBody.Velocity;
                break;
        }
    }
    //Restart
    public ComponentPlayerPackage(PlayerData playerData, PlayerAction type)
    {
        m_type = type;
        m_playerData = playerData;
    }
    public ComponentPlayerPackage(PlayerData playerData, int count, bool playSound, float level)
    {
        m_playerData = playerData;
        m_type = PlayerAction.AddExperience;
        m_count = count;
        m_playSound = playSound;
        m_level = level;
    }
    public ComponentPlayerPackage(PlayerData playerData, int inventory_id, int slotIndex, Vector3 postion, int count)
    {
        m_playerData = playerData;
        m_type = PlayerAction.DragDrop;
        m_inventory_id = inventory_id;
        m_ActiveSlot = slotIndex;
        m_hitPosition = postion;
        m_count = count;
    }
    public ComponentPlayerPackage(ComponentPlayer player, ComponentBody body, Vector3 hitPosition, Vector3 hitDirection)
    {
        m_playerData = player.PlayerData;
        bodyId = body.Entity.EntityId;
        m_hitPosition = hitPosition;
        m_hitDirection = hitDirection;
        m_type = PlayerAction.Hit;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        if (m_playerData.ClientId.HasValue)
        {
            writer.Write(m_playerData.ClientId.Value);
        }
        else writer.Write((byte)0);
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case PlayerAction.BodyUpdate:
                writer.WriteEnum(changFlag);
                if (changFlag.HasFlag(ChangFlag.ParentBodyChange))
                {
                    if (changFlag.HasFlag(ChangFlag.LookAnglesChange))
                    {
                        writer.Write(LookAngles);
                    }
                    if (changFlag.HasFlag(ChangFlag.ChildLookAnglesChange))
                    {
                        writer.Write(ChildLookAngles);
                    }
                }
                else
                {
                    if (changFlag.HasFlag(ChangFlag.LookAnglesChange))
                    {
                        writer.Write(LookAngles);
                    }
                }
                if (changFlag.HasFlag(ChangFlag.PositionChange))
                {
                    writer.Write(Position);
                }
                if (changFlag.HasFlag(ChangFlag.RotationChange))
                {
                    writer.Write(Rotation);
                }
                if (changFlag.HasFlag(ChangFlag.VelocityChange))
                {
                    writer.Write(Velocity);
                }
                if (changFlag.HasFlag(ChangFlag.LadderChange))
                {
                    writer.Write(m_ladderValue);
                }
                if (changFlag.HasFlag(ChangFlag.SneakChange))
                {
                    writer.Write(m_sneaking);
                }
                break;
            case PlayerAction.InteractEvent:
                writer.WriteEnum(m_interactEvent);
                writer.Write(m_netInteractRay);
                writer.Write(m_netInteractRaycast);
                break;
            case PlayerAction.AimEvent:
                writer.WriteEnum(m_aimEvent);
                writer.Write(m_netAimRay);
                break;
            case PlayerAction.DigEvent:
                writer.WriteEnum(m_digEvent);
                writer.Write(m_netDigRay);
                writer.Write(m_netDigRaycast);
                break;
            case PlayerAction.Hit:
                writer.Write(bodyId);
                writer.Write(m_hitPosition);
                writer.Write(m_hitDirection);
                break;
            case PlayerAction.AddExperience:
                writer.Write(m_count);
                writer.Write(m_playSound);
                writer.Write(m_level);
                break;
            case PlayerAction.DragDrop:
                writer.Write(m_inventory_id);
                writer.Write(m_ActiveSlot);
                writer.Write(m_hitPosition);
                writer.Write(m_count);
                break;
            case PlayerAction.CreativeFlyChange:
                writer.Write(m_isCreativeFly);
                break;
            case PlayerAction.SyncStat:
                writer.WriteBuff(m_stat.ToMessagePack());
                break;
            case PlayerAction.PositionSet:
                writer.Write(Position);
                writer.Write(Velocity);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_fromPlayerId = reader.ReadByte();
        m_type = reader.ReadEnum<PlayerAction>();
        switch (m_type)
        {
            case PlayerAction.BodyUpdate:
                changFlag = reader.ReadEnum<ChangFlag>();
                if (changFlag.HasFlag(ChangFlag.ParentBodyChange))
                {
                    if (changFlag.HasFlag(ChangFlag.LookAnglesChange))
                    {
                        LookAngles = reader.ReadVector2();
                    }
                    if (changFlag.HasFlag(ChangFlag.ChildLookAnglesChange))
                    {
                        ChildLookAngles = reader.ReadVector2();
                    }
                }
                else
                {
                    if (changFlag.HasFlag(ChangFlag.LookAnglesChange))
                    {
                        LookAngles = reader.ReadVector2();
                    }
                }

                if (changFlag.HasFlag(ChangFlag.PositionChange))
                {
                    Position = reader.ReadVector3();
                }

                if (changFlag.HasFlag(ChangFlag.RotationChange))
                {
                    Rotation = reader.ReadQuaternion();
                }

                if (changFlag.HasFlag(ChangFlag.VelocityChange))
                {
                    Velocity = reader.ReadVector3();
                }

                if (changFlag.HasFlag(ChangFlag.LadderChange))
                {
                    m_ladderValue = reader.ReadInt32();
                }

                if (changFlag.HasFlag(ChangFlag.SneakChange))
                {
                    m_sneaking = reader.ReadBoolean();
                }
                break;
            case PlayerAction.InteractEvent:
                m_interactEvent = reader.ReadEnum<ComponentPlayer.InteractEvent>();
                m_netInteractRay = reader.ReadRay3();
                m_netInteractRaycast = reader.ReadTerrainRaycastResultNullable();
                break;
            case PlayerAction.AimEvent:
                m_aimEvent = reader.ReadEnum<ComponentPlayer.AimEvent>();
                m_netAimRay = reader.ReadRay3Nullable();
                break;
            case PlayerAction.DigEvent:
                m_digEvent = reader.ReadEnum<ComponentPlayer.DigEvent>();
                m_netDigRay = reader.ReadRay3Nullable();
                m_netDigRaycast = reader.ReadTerrainRaycastResultNullable();
                break;
            case PlayerAction.Hit:
                bodyId = reader.ReadUInt16();
                m_hitPosition = reader.ReadVector3();
                m_hitDirection = reader.ReadVector3();
                break;
            case PlayerAction.AddExperience:
                m_count = reader.ReadInt32();
                m_playSound = reader.ReadBoolean();
                m_level = reader.ReadSingle();
                break;
            case PlayerAction.DragDrop:
                m_inventory_id = reader.ReadInt32();
                m_ActiveSlot = reader.ReadInt32();
                m_hitPosition = reader.ReadVector3();
                m_count = reader.ReadInt32();
                break;
            case PlayerAction.CreativeFlyChange:
                m_isCreativeFly = reader.ReadBoolean();
                break;
            case PlayerAction.SyncStat:
                byte[] messagePack = reader.ReadBuff();
                m_stat = new TemplatesDatabase.ValuesDictionary();
                m_stat.ApplyOverridesUseMessagePack(messagePack);
                break;
            case PlayerAction.PositionSet:
                Position = reader.ReadVector3();
                Velocity = reader.ReadVector3();
                break;
        }
    }
    public void PlayerEvent(Action<ComponentPlayer> action, Action fail = null)
    {
        if (m_playerData != null && m_playerData.ComponentPlayer != null)
        {
            action?.Invoke(m_playerData.ComponentPlayer);
        }
        else
        {
            fail?.Invoke();
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        m_playerData = projectNet.FindSubsystem<SubsystemPlayers>().FindPlayerData((playerData) => { return playerData.ClientId.HasValue && playerData.ClientId.Value == m_fromPlayerId; });
        if (NeedHandleMainPlayer == false && m_playerData.IsMainPlayer && m_type != PlayerAction.AddExperience)
        {
            return;
        }
        if (isServer && (m_playerData == null || m_playerData.ClientId != From.ID))
        {
            return;
        }
        if (isServer && (
            m_type == PlayerAction.IntoPlaying ||
            m_type == PlayerAction.AddExperience ||
            m_type == PlayerAction.SyncStat ||
            m_type == PlayerAction.PositionSet))
        {
            return;
        }
        if (isServer && m_type == PlayerAction.CreativeFlyChange && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative)
        {
            NetworkManager.Server.RemoveClient(From, "琛屼负寮傚父");
            return;
        }
        switch (m_type)
        {
            case PlayerAction.BodyUpdate:
                PlayerEvent((player) =>
                {
                    ComponentBody body;
                    if (changFlag.HasFlag(ChangFlag.ParentBodyChange))
                    {
                        if (player.ComponentBody.ParentBody != null)
                        {
                            body = player.ComponentBody.ParentBody;
                            if (changFlag.HasFlag(ChangFlag.LookAnglesChange))
                            {
                                var loco = body.Locomotion;
                                loco.NetLookAngles.SetNext(LookAngles);
                            }
                            if (changFlag.HasFlag(ChangFlag.ChildLookAnglesChange))
                            {
                                player.ComponentBody.Locomotion.NetLookAngles.SetNext(ChildLookAngles);
                            }

                        }
                        else
                        {
                            body = player.ComponentBody;
                        }
                    }
                    else
                    {
                        body = player.ComponentBody;
                        if (changFlag.HasFlag(ChangFlag.LookAnglesChange))
                        {
                            player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
                        }
                    }
                    if (changFlag.HasFlag(ChangFlag.VelocityChange))
                    {
                        body.netVelocity.SetNext(Velocity);
                    }
                    if (body.Locomotion != null)
                    {
                        if (changFlag.HasFlag(ChangFlag.LadderChange))
                        {
                            body.Locomotion.LadderValue = m_ladderValue;
                        }
                        else body.Locomotion.LadderValue = null;
                    }

                    if (changFlag.HasFlag(ChangFlag.PositionChange))
                    {

                        body.netPosition.SetNext(Position);
                    }
                    if (changFlag.HasFlag(ChangFlag.RotationChange))
                    {
                        body.netRotation.SetNext(Rotation);
                    }
                    if (changFlag.HasFlag(ChangFlag.SneakChange))
                    {
                        body.IsSneaking = m_sneaking;
                    }
                });
                break;
            case PlayerAction.InteractEvent:
                PlayerEvent((player) =>
                {

                    player.AddInteractEvent(m_interactEvent, m_netInteractRay, m_netInteractRaycast);
                    if (isServer)
                    {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.AimEvent:
                PlayerEvent((player) =>
                {
                    player.AddAimEvent(m_aimEvent, m_netAimRay);
                    if (isServer)
                    {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.DigEvent:
                PlayerEvent((player) =>
                {
                    player.AddDigEvent(m_digEvent, m_netDigRay, m_netDigRaycast);
                    if (isServer)
                    {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.CreativeFlyChange:
                PlayerEvent((player) =>
                {
                    player.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
                    if (isServer)
                    {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.Hit:
                if (isServer && m_playerData != null)
                {
                    var time = projectNet.FindSubsystem<SubsystemTime>().GameTime;
                    var guid = m_playerData.PlayerGUID;
                    if (s_lastHitTimes.TryGetValue(guid, out var last))
                    {
                        if (time - last < SERVER_HIT_COOLDOWN)
                        {
                            break;
                        }
                    }
                    s_lastHitTimes[guid] = time;
                }
                PlayerEvent((player) =>
                {
                    var eye = player.ComponentMiner.ComponentCreature.ComponentCreatureModel.EyePosition;
                    Vector3 dir = m_hitDirection;
                    if (dir.LengthSquared() < 1e-06f)
                    {
                        dir = player.ComponentMiner.ComponentCreature.ComponentBody.Matrix.Forward;
                    }
                    dir = Vector3.Normalize(dir);
                    var serverRay = new Ray3(eye, dir);
                    var bodyHit = player.ComponentMiner.Raycast<BodyRaycastResult>(serverRay, RaycastMode.Interaction);
                    if (bodyHit.HasValue)
                    {
                        var hit = bodyHit.Value;
                        if (Vector3.Distance(hit.HitPoint(), eye) <= 6f && hit.ComponentBody?.Entity?.EntityId == bodyId)
                        {
                            projectNet.FindEntityById(bodyId, (entity) =>
                            {
                                var body = entity.FindComponent<ComponentBody>();
                                player.ComponentMiner.Hit(body, hit.HitPoint(), dir);
                                if (isServer)
                                {
                                    Except = From;
                                    NetworkManager.QueuePackage(this);
                                }
                            });
                        }
                    }
                });
                break;
            case PlayerAction.IntoPlaying:
                PlayerEvent((player) =>
                {
                    player.ComponentHealth.IsInvulnerable = false;

                });
                break;
            case PlayerAction.Restart:
                PlayerEvent((player) =>
                {
                    player.PlayerData.readyToRestart = true;
                });
                break;
            case PlayerAction.AddExperience:
                PlayerEvent((player) =>
                {
                    player.ComponentLevel.NetAddExperience(m_count, m_playSound);
                    player.PlayerData.Level = m_level;
                });
                break;
            case PlayerAction.Drop:
                PlayerEvent((player) =>
                {

                    player.DoDrop();
                });
                break;
            case PlayerAction.DragDrop:
                // 鎴戝幓锛屽埆杩欐牱鎼炲晩锛屽洖璋冨湴鐙卞彲鏄細寰堝ご鐤肩殑锛侊紒锛侊紒锛?
                PlayerEvent((player) =>
                {
                    projectNet.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventory_id, (inventory) =>
                    {
                        // 涓㈠純鑳屽寘鍐呯殑鐗╁搧锛屼笉鏄椿鍔ㄦ爮鐨?
                        if (inventory == null || ComponentInventoryPackage.IsOwnedByOtherPlayer(inventory, From, out _))
                        {
                            return;
                        }
                        if (inventory is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative)
                        {
                            NetworkManager.Server.RemoveClient(From.PlayerData.Client, "非法操作: 生存模式使用创造物品");
                            return;
                        }

                        player.ViewWidget.NetDragDrop(m_hitPosition, new InventoryDragData() { Inventory = inventory, SlotIndex = m_ActiveSlot }, m_count);
                    });
                });
                break;
            case PlayerAction.SyncStat:
                PlayerEvent((player) =>
                {
                    player.PlayerStats.Load(m_stat);
                });
                break;
            case PlayerAction.PositionSet:
                PlayerEvent((player) =>
                {
                    player.ComponentBody.Position = Position;
                    player.ComponentBody.Velocity = Velocity;
                });
                break;
        }

    }
}
