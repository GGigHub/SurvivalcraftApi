using Engine;
using TemplatesDatabase;

namespace Game.NetWork.Packages;

public static class PlayerPackageHelpers {
    public enum ChangeFlag : byte {
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

    private static readonly Dictionary<Guid, double> s_lastHitTimes = [];
    private const double ServerHitCooldown = 0.2;

    public static PlayerData ResolvePlayer(byte clientId) {
        return GameManager.Project.FindSubsystem<SubsystemPlayers>()
            .FindPlayerData(playerData => playerData.ClientId.HasValue && playerData.ClientId.Value == clientId);
    }

    public static bool ValidateServerSender(PlayerData playerData, Client from) {
        return playerData != null && from != null && playerData.ClientId == from.ID;
    }

    public static void SerializeFromPlayerId(PackageSerializer archive, ref byte fromPlayerId, PlayerData playerData) {
        if (archive.IsWriting && playerData != null) {
            fromPlayerId = playerData?.ClientId ?? 0;
        }
        archive.Value(ref fromPlayerId);
    }

    public static bool ThrottleHit(PlayerData playerData) {
        if (playerData == null) {
            return false;
        }
        double time = GameManager.Project.FindSubsystem<SubsystemTime>().GameTime;
        Guid guid = playerData.PlayerGUID;
        if (s_lastHitTimes.TryGetValue(guid, out double last) && time - last < ServerHitCooldown) {
            return true;
        }
        s_lastHitTimes[guid] = time;
        return false;
    }

    public static void QueuePlayerPackage(IPackage package, bool isServer) {
        if (isServer) {
            package.Except = package.From;
            NetworkManager.QueuePackage(package);
        }
    }

    public static bool TryResolvePlayer(byte fromPlayerId, bool isServer, Client from, out PlayerData playerData) {
        playerData = ResolvePlayer(fromPlayerId);
        if (isServer && !ValidateServerSender(playerData, from)) {
            return false;
        }
        return playerData != null;
    }
}

public abstract class PlayerPackageBase : IPackage {
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public byte m_fromPlayerId;

    protected void SerializePlayerId(PackageSerializer archive, PlayerData playerData) {
        PlayerPackageHelpers.SerializeFromPlayerId(archive, ref m_fromPlayerId, playerData);
    }

    protected bool TryResolvePlayer(bool isServer, out PlayerData playerData) {
        return PlayerPackageHelpers.TryResolvePlayer(m_fromPlayerId, isServer, From, out playerData);
    }
}

public class PlayerBodyUpdatePackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerBodyUpdate;
    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Rotation;
    public Vector2 LookAngles;
    public Vector2 ChildLookAngles;
    public PlayerPackageHelpers.ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;

    public PlayerBodyUpdatePackage() { }

    public PlayerBodyUpdatePackage(ComponentPlayer player) {
        PlayerData playerData = player.PlayerData;
        changeFlag = PlayerPackageHelpers.ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        if (body != null) {
            changeFlag |= PlayerPackageHelpers.ChangeFlag.ParentBodyChange;
            if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = body.Locomotion.SendLookAngles.Value;
                body.Locomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
        }
        else {
            body = player.ComponentBody;
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (body.SendPosition.HasValue) {
            Position = body.SendPosition.Value;
            changeFlag |= PlayerPackageHelpers.ChangeFlag.PositionChange;
            body.SendPosition = null;
        }
        if (body.SendRotation.HasValue) {
            Rotation = body.SendRotation.Value;
            changeFlag |= PlayerPackageHelpers.ChangeFlag.RotationChange;
            body.SendRotation = null;
        }
        if (body.SendVelocity.HasValue) {
            Velocity = body.SendVelocity.Value;
            changeFlag |= PlayerPackageHelpers.ChangeFlag.VelocityChange;
            body.SendVelocity = null;
        }
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }

    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref changeFlag);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LookAnglesChange)) archive.Value(ref LookAngles);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.ChildLookAnglesChange)) archive.Value(ref ChildLookAngles);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.PositionChange)) archive.Value(ref Position);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.RotationChange)) archive.Value(ref Rotation);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.VelocityChange)) archive.Value(ref Velocity);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LadderChange)) archive.Value(ref m_ladderValue);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.SneakChange)) archive.Value(ref m_sneaking);
    }

    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentBody body;
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.ParentBodyChange) && player.ComponentBody.ParentBody != null) {
            body = player.ComponentBody.ParentBody;
            if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LookAnglesChange)) {
                body.Locomotion?.NetLookAngles.SetNext(LookAngles);
            }
            if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.ChildLookAnglesChange)) {
                player.ComponentBody.Locomotion?.NetLookAngles.SetNext(ChildLookAngles);
            }
        }
        else {
            body = player.ComponentBody;
            if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LookAnglesChange)) {
                player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
            }
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.VelocityChange)) {
            body.netVelocity.SetNext(Velocity);
        }
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.PositionChange)) {
            body.netPosition.SetNext(Position);
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.RotationChange)) {
            body.netRotation.SetNext(Rotation);
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.SneakChange)) {
            body.IsSneaking = m_sneaking;
        }
    }
}

public class PlayerInteractPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerInteract;
    public ComponentPlayer.InteractEvent m_interactEvent;
    public Ray3 m_netInteractRay;
    public TerrainRaycastResult? m_netInteractRaycast;

    public PlayerInteractPackage() { }
    public PlayerInteractPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_interactEvent = player.interactEvent.interactEvent;
        m_netInteractRay = player.interactEvent.NetInteractRay;
        m_netInteractRaycast = player.interactEvent.NetPlaceRaycast;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_interactEvent);
        archive.Value(ref m_netInteractRay);
        archive.Value(ref m_netInteractRaycast);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddInteractEvent(m_interactEvent, m_netInteractRay, m_netInteractRaycast);
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}

public class PlayerAimPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerAim;
    public ComponentPlayer.AimEvent m_aimEvent;
    public Ray3? m_netAimRay;
    public PlayerAimPackage() { }
    public PlayerAimPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_aimEvent = player.aimEvent.aimEvent;
        m_netAimRay = player.aimEvent.NetAim;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_aimEvent);
        archive.Value(ref m_netAimRay);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddAimEvent(m_aimEvent, m_netAimRay);
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}

public class PlayerDigPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerDig;
    public ComponentPlayer.DigEvent m_digEvent;
    public Ray3? m_netDigRay;
    public TerrainRaycastResult? m_netDigRaycast;
    public PlayerDigPackage() { }
    public PlayerDigPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_digEvent = player.digEvent.digEvent;
        m_netDigRay = player.digEvent.NetDigRay;
        m_netDigRaycast = player.digEvent.NetDigRaycast;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_digEvent);
        archive.Value(ref m_netDigRay);
        archive.Value(ref m_netDigRaycast);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddDigEvent(m_digEvent, m_netDigRay, m_netDigRaycast);
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}

public class PlayerHitPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerHit;
    public int bodyId;
    public Vector3 m_hitPosition;
    public Vector3 m_hitDirection;
    public PlayerHitPackage() { }
    public PlayerHitPackage(ComponentPlayer player, ComponentBody body, Vector3 hitPosition, Vector3 hitDirection) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        bodyId = body.Entity.Id;
        m_hitPosition = hitPosition;
        m_hitDirection = hitDirection;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref bodyId);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_hitDirection);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && PlayerPackageHelpers.ThrottleHit(playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        Vector3 eye = player.ComponentMiner.ComponentCreature.ComponentCreatureModel.EyePosition;
        Vector3 dir = m_hitDirection;
        if (dir.LengthSquared() < 1E-06f) {
            dir = player.ComponentMiner.ComponentCreature.ComponentBody.Matrix.Forward;
        }
        dir = Vector3.Normalize(dir);
        Ray3 serverRay = new(eye, dir);
        BodyRaycastResult? bodyHit = player.ComponentMiner.Raycast<BodyRaycastResult>(serverRay, RaycastMode.Interaction);
        if (bodyHit.HasValue) {
            BodyRaycastResult hit = bodyHit.Value;
            if (Vector3.Distance(hit.HitPoint(), eye) <= 6f && hit.ComponentBody?.Entity?.Id == bodyId) {
                GameManager.Project.FindEntity(bodyId, entity => {
                    ComponentBody body = entity.FindComponent<ComponentBody>();
                    player.ComponentMiner.Hit(body, hit.HitPoint(), dir);
                    PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
                });
            }
        }
    }
}

public class PlayerCreativeFlyPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerCreativeFly;
    public bool m_isCreativeFly;
    public PlayerCreativeFlyPackage() { }
    public PlayerCreativeFlyPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_isCreativeFly = player.ComponentLocomotion.IsCreativeFlyEnabled;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_isCreativeFly);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
            NetworkManager.Server.RemoveClient(From, "鐞涘奔璐熷鍌氱埗");
            return;
        }
        playerData.ComponentPlayer.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}

public class PlayerIntoPlayingPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerIntoPlaying;
    public PlayerIntoPlayingPackage() { }
    public PlayerIntoPlayingPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
    }
}

public class PlayerRestartPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerRestart;
    public PlayerRestartPackage() { }
    public PlayerRestartPackage(PlayerData playerData) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.readyToRestart = true;
    }
}

public class PlayerAddExperiencePackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerAddExperience;
    public int m_count;
    public bool m_playSound;
    public float m_level;
    public PlayerAddExperiencePackage() { }
    public PlayerAddExperiencePackage(PlayerData playerData, int count, bool playSound, float level) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
        m_count = count;
        m_playSound = playSound;
        m_level = level;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_count);
        archive.Value(ref m_playSound);
        archive.Value(ref m_level);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        ComponentPlayer player = playerData.ComponentPlayer;
        player.ComponentLevel.NetAddExperience(m_count, m_playSound);
        player.PlayerData.Level = m_level;
    }
}

public class PlayerDragDropPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerDragDrop;
    public int m_inventoryId;
    public int m_slotIndex;
    public Vector3 m_hitPosition;
    public int m_count;
    public PlayerDragDropPackage() { }
    public PlayerDragDropPackage(PlayerData playerData, int inventoryId, int slotIndex, Vector3 position, int count) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_hitPosition = position;
        m_count = count;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_count);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
            if (inventory == null || InventoryAccessGuard.IsOwnedByOtherPlayer(inventory, From, out _)) {
                return;
            }
            if (inventory is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
                NetworkManager.Server.RemoveClient(From.PlayerData.Client, "闈炴硶鎿嶄綔: 鐢熷瓨妯″紡浣跨敤鍒涢€犵墿鍝?");
                return;
            }
            if (!InventoryAccessGuard.IsSlotIndexValid(inventory, m_slotIndex)) {
                if (isServer && From != null) {
                    NetworkManager.QueuePackage(new InventorySyncPackage(inventory) { To = From });
                }
                return;
            }
            playerData.ComponentPlayer.ViewWidget.NetDragDrop(m_hitPosition, new InventoryDragData { Inventory = inventory, SlotIndex = m_slotIndex }, m_count);
        });
    }
}

public class PlayerStatSyncPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerStatSync;
    public ValuesDictionary m_stat;
    public PlayerStatSyncPackage() { }
    public PlayerStatSyncPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_stat = new ValuesDictionary();
        player.PlayerStats.Save(m_stat);
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        byte[] messagePack = archive.IsWriting ? m_stat.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_stat = new ValuesDictionary();
            m_stat.ApplyOverridesUseMessagePack(messagePack);
        }
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.PlayerStats.Load(m_stat);
    }
}

public class PlayerPositionSetPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerPositionSet;
    public Vector3 Position;
    public Vector3 Velocity;
    public PlayerPositionSetPackage() { }
    public PlayerPositionSetPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        Position = player.ComponentBody.Position;
        Velocity = player.ComponentBody.Velocity;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentBody.Position = Position;
        playerData.ComponentPlayer.ComponentBody.Velocity = Velocity;
    }
}
