using Engine;

namespace Game.NetWork.Packages;

public class ComponentPlayerPackage : IPackage {

    public override byte ID => (byte)PackageType.Player;
    public enum PlayerAction {
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
    public enum ChangFlag : byte {
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

    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

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
    public int bodyId;
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

    public ComponentPlayerPackage() {

    }
    public ComponentPlayerPackage(ComponentPlayer player, PlayerAction type) {
        m_type = type;
        m_playerData = player.PlayerData;
        switch (type) {
            case PlayerAction.BodyUpdate:
                var body = player.ComponentBody.ParentBody;
                if (body != null) {
                    changFlag |= ChangFlag.ParentBodyChange;
                    if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue) {
                        changFlag |= ChangFlag.LookAnglesChange;
                        LookAngles = body.Locomotion.SendLookAngles.Value;
                        body.Locomotion.SendLookAngles = null;
                    }
                    if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                        changFlag |= ChangFlag.ChildLookAnglesChange;
                        ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                        player.ComponentLocomotion.SendLookAngles = null;
                    }
                }
                else {
                    body = player.ComponentBody;
                    if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                        changFlag |= ChangFlag.LookAnglesChange;
                        LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                        player.ComponentLocomotion.SendLookAngles = null;
                    }
                    if (player.ComponentLocomotion.LadderValue.HasValue) {
                        changFlag |= ChangFlag.LadderChange;
                        m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
                    }
                    if (body.CanCrouch) {
                        changFlag |= ChangFlag.SneakChange;
                        m_sneaking = body.IsCrouching;
                    }
                }
                if (body.SendPosition.HasValue) {
                    Position = body.SendPosition.Value;
                    changFlag |= ChangFlag.PositionChange;
                    body.SendPosition = null;
                }
                if (body.SendRotation.HasValue) {
                    changFlag |= ChangFlag.RotationChange;
                    Rotation = body.SendRotation.Value;
                    body.SendRotation = null;
                }
                if (body.SendVelocity.HasValue) {
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
    public ComponentPlayerPackage(PlayerData playerData, PlayerAction type) {
        m_type = type;
        m_playerData = playerData;
    }
    public ComponentPlayerPackage(PlayerData playerData, int count, bool playSound, float level) {
        m_playerData = playerData;
        m_type = PlayerAction.AddExperience;
        m_count = count;
        m_playSound = playSound;
        m_level = level;
    }
    public ComponentPlayerPackage(PlayerData playerData, int inventory_id, int slotIndex, Vector3 postion, int count) {
        m_playerData = playerData;
        m_type = PlayerAction.DragDrop;
        m_inventory_id = inventory_id;
        m_ActiveSlot = slotIndex;
        m_hitPosition = postion;
        m_count = count;
    }
    public ComponentPlayerPackage(ComponentPlayer player, ComponentBody body, Vector3 hitPosition, Vector3 hitDirection) {
        m_playerData = player.PlayerData;
        bodyId = body.Entity.Id;
        m_hitPosition = hitPosition;
        m_hitDirection = hitDirection;
        m_type = PlayerAction.Hit;
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsWriting) {
            m_fromPlayerId = m_playerData?.ClientId ?? 0;
        }
        archive.Value(ref m_fromPlayerId);
        archive.Enum(ref m_type);
        switch (m_type) {
            case PlayerAction.BodyUpdate:
                SerializeBodyUpdate(archive);
                break;
            case PlayerAction.InteractEvent:
                archive.Enum(ref m_interactEvent);
                archive.Value(ref m_netInteractRay);
                archive.Value(ref m_netInteractRaycast);
                break;
            case PlayerAction.AimEvent:
                archive.Enum(ref m_aimEvent);
                archive.Value(ref m_netAimRay);
                break;
            case PlayerAction.DigEvent:
                archive.Enum(ref m_digEvent);
                archive.Value(ref m_netDigRay);
                archive.Value(ref m_netDigRaycast);
                break;
            case PlayerAction.Hit:
                archive.Value(ref bodyId);
                archive.Value(ref m_hitPosition);
                archive.Value(ref m_hitDirection);
                break;
            case PlayerAction.AddExperience:
                archive.Value(ref m_count);
                archive.Value(ref m_playSound);
                archive.Value(ref m_level);
                break;
            case PlayerAction.DragDrop:
                archive.Value(ref m_inventory_id);
                archive.Value(ref m_ActiveSlot);
                archive.Value(ref m_hitPosition);
                archive.Value(ref m_count);
                break;
            case PlayerAction.CreativeFlyChange:
                archive.Value(ref m_isCreativeFly);
                break;
            case PlayerAction.SyncStat:
                byte[] messagePack = archive.IsWriting ? m_stat.ToMessagePack() : null;
                archive.Buffer(ref messagePack);
                if (archive.IsReading) {
                    m_stat = new TemplatesDatabase.ValuesDictionary();
                    m_stat.ApplyOverridesUseMessagePack(messagePack);
                }
                break;
            case PlayerAction.PositionSet:
                archive.Value(ref Position);
                archive.Value(ref Velocity);
                break;
        }
    }

    private void SerializeBodyUpdate(PackageSerializer archive) {
        archive.Enum(ref changFlag);
        if (changFlag.HasFlag(ChangFlag.ParentBodyChange)) {
            if (changFlag.HasFlag(ChangFlag.LookAnglesChange)) archive.Value(ref LookAngles);
            if (changFlag.HasFlag(ChangFlag.ChildLookAnglesChange)) archive.Value(ref ChildLookAngles);
        }
        else {
            if (changFlag.HasFlag(ChangFlag.LookAnglesChange)) archive.Value(ref LookAngles);
        }
        if (changFlag.HasFlag(ChangFlag.PositionChange)) archive.Value(ref Position);
        if (changFlag.HasFlag(ChangFlag.RotationChange)) archive.Value(ref Rotation);
        if (changFlag.HasFlag(ChangFlag.VelocityChange)) archive.Value(ref Velocity);
        if (changFlag.HasFlag(ChangFlag.LadderChange)) archive.Value(ref m_ladderValue);
        if (changFlag.HasFlag(ChangFlag.SneakChange)) archive.Value(ref m_sneaking);
    }
    public void PlayerEvent(Action<ComponentPlayer> action, Action fail = null) {
        if (m_playerData != null && m_playerData.ComponentPlayer != null) {
            action?.Invoke(m_playerData.ComponentPlayer);
        }
        else {
            fail?.Invoke();
        }
    }

    public override void Handle(bool isServer) {
        m_playerData = GameManager.Project.FindSubsystem<SubsystemPlayers>().FindPlayerData((playerData) => { return playerData.ClientId.HasValue && playerData.ClientId.Value == m_fromPlayerId; });
        if (NeedHandleMainPlayer == false && m_playerData.IsMainPlayer && m_type != PlayerAction.AddExperience) {
            return;
        }
        if (isServer && (m_playerData == null || m_playerData.ClientId != From.ID)) {
            return;
        }
        if (isServer && (
            m_type == PlayerAction.IntoPlaying ||
            m_type == PlayerAction.AddExperience ||
            m_type == PlayerAction.SyncStat ||
            m_type == PlayerAction.PositionSet)) {
            return;
        }
        if (isServer && m_type == PlayerAction.CreativeFlyChange && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
            NetworkManager.Server.RemoveClient(From, "状态检测异常");
            return;
        }
        switch (m_type) {
            case PlayerAction.BodyUpdate:
                PlayerEvent((player) => {
                    ComponentBody body;
                    if (changFlag.HasFlag(ChangFlag.ParentBodyChange)) {
                        if (player.ComponentBody.ParentBody != null) {
                            body = player.ComponentBody.ParentBody;
                            if (changFlag.HasFlag(ChangFlag.LookAnglesChange)) {
                                var loco = body.Locomotion;
                                loco.NetLookAngles.SetNext(LookAngles);
                            }
                            if (changFlag.HasFlag(ChangFlag.ChildLookAnglesChange)) {
                                player.ComponentBody.Locomotion.NetLookAngles.SetNext(ChildLookAngles);
                            }

                        }
                        else {
                            body = player.ComponentBody;
                        }
                    }
                    else {
                        body = player.ComponentBody;
                        if (changFlag.HasFlag(ChangFlag.LookAnglesChange)) {
                            player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
                        }
                    }
                    if (changFlag.HasFlag(ChangFlag.VelocityChange)) {
                        body.netVelocity.SetNext(Velocity);
                    }
                    if (body.Locomotion != null) {
                        if (changFlag.HasFlag(ChangFlag.LadderChange)) {
                            body.Locomotion.LadderValue = m_ladderValue;
                        }
                        else body.Locomotion.LadderValue = null;
                    }

                    if (changFlag.HasFlag(ChangFlag.PositionChange)) {

                        body.netPosition.SetNext(Position);
                    }
                    if (changFlag.HasFlag(ChangFlag.RotationChange)) {
                        body.netRotation.SetNext(Rotation);
                    }
                    if (changFlag.HasFlag(ChangFlag.SneakChange)) {
                        body.IsSneaking = m_sneaking;
                    }
                });
                break;
            case PlayerAction.InteractEvent:
                PlayerEvent((player) => {

                    player.AddInteractEvent(m_interactEvent, m_netInteractRay, m_netInteractRaycast);
                    if (isServer) {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.AimEvent:
                PlayerEvent((player) => {
                    player.AddAimEvent(m_aimEvent, m_netAimRay);
                    if (isServer) {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.DigEvent:
                PlayerEvent((player) => {
                    player.AddDigEvent(m_digEvent, m_netDigRay, m_netDigRaycast);
                    if (isServer) {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.CreativeFlyChange:
                PlayerEvent((player) => {
                    player.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
                    if (isServer) {
                        Except = From;
                        NetworkManager.QueuePackage(this);
                    }
                });
                break;
            case PlayerAction.Hit:
                if (isServer && m_playerData != null) {
                    var time = GameManager.Project.FindSubsystem<SubsystemTime>().GameTime;
                    var guid = m_playerData.PlayerGUID;
                    if (s_lastHitTimes.TryGetValue(guid, out var last)) {
                        if (time - last < SERVER_HIT_COOLDOWN) {
                            break;
                        }
                    }
                    s_lastHitTimes[guid] = time;
                }
                PlayerEvent((player) => {
                    var eye = player.ComponentMiner.ComponentCreature.ComponentCreatureModel.EyePosition;
                    Vector3 dir = m_hitDirection;
                    if (dir.LengthSquared() < 1e-06f) {
                        dir = player.ComponentMiner.ComponentCreature.ComponentBody.Matrix.Forward;
                    }
                    dir = Vector3.Normalize(dir);
                    var serverRay = new Ray3(eye, dir);
                    var bodyHit = player.ComponentMiner.Raycast<BodyRaycastResult>(serverRay, RaycastMode.Interaction);
                    if (bodyHit.HasValue) {
                        var hit = bodyHit.Value;
                        if (Vector3.Distance(hit.HitPoint(), eye) <= 6f && hit.ComponentBody?.Entity?.Id == bodyId) {
                            GameManager.Project.FindEntity(bodyId, (entity) => {
                                var body = entity.FindComponent<ComponentBody>();
                                player.ComponentMiner.Hit(body, hit.HitPoint(), dir);
                                if (isServer) {
                                    Except = From;
                                    NetworkManager.QueuePackage(this);
                                }
                            });
                        }
                    }
                });
                break;
            case PlayerAction.IntoPlaying:
                PlayerEvent((player) => {
                    player.ComponentHealth.IsInvulnerable = false;

                });
                break;
            case PlayerAction.Restart:
                PlayerEvent((player) => {
                    player.PlayerData.readyToRestart = true;
                });
                break;
            case PlayerAction.AddExperience:
                PlayerEvent((player) => {
                    player.ComponentLevel.NetAddExperience(m_count, m_playSound);
                    player.PlayerData.Level = m_level;
                });
                break;
            case PlayerAction.Drop:
                PlayerEvent((player) => {

                    player.DoDrop();
                });
                break;
            case PlayerAction.DragDrop:
                // 别这样搞，回调地狱很头疼的啊。。。
                PlayerEvent((player) => {
                    GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventory_id, (inventory) => {
                        // 丢弃背包内的物品，不是活动栏里的物品
                        if (inventory == null || ComponentInventoryPackage.IsOwnedByOtherPlayer(inventory, From, out _)) {
                            return;
                        }
                        if (inventory is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
                            NetworkManager.Server.RemoveClient(From.PlayerData.Client, "非法操作: 生存模式使用创造物品");
                            return;
                        }

                        player.ViewWidget.NetDragDrop(m_hitPosition, new InventoryDragData() { Inventory = inventory, SlotIndex = m_ActiveSlot }, m_count);
                    });
                });
                break;
            case PlayerAction.SyncStat:
                PlayerEvent((player) => {
                    player.PlayerStats.Load(m_stat);
                });
                break;
            case PlayerAction.PositionSet:
                PlayerEvent((player) => {
                    player.ComponentBody.Position = Position;
                    player.ComponentBody.Velocity = Velocity;
                });
                break;
        }

    }
}
