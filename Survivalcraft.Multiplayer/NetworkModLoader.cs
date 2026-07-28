using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Game;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Survivalcraft.Multiplayer {
    public class NetworkModLoader : ModLoader {
        public override void __ModInitialize() {

            string[] hookNames = [
                "IsNetworkMode",
                "OnEntityAdd",
                "OnEntityRemove",
                "OnNetworkChaseBehaviorAttackChanged",
                "OnNetworkCreatureSound",
                "OnNetworkDamage",
                "OnNetworkDigInMudBehaviorChanged",
                "OnNetworkEatPickableBehaviorChanged",
                "OnNetworkFishOutOfWaterBehaviorChanged",
                "OnNetworkLookAroundBehaviorChanged",
                "OnNetworkFluEffect",
                "OnNetworkFluUpdate",
                "OnNetworkFluSneeze",
                "OnNetworkFluStarted",
                "OnNetworkFluImpulse",
                "OnNetworkFurnaceUpdate",
                "OnNetworkManualLightingStrike",
                "OnNetworkManualTimeChange",
                "OnNetworkManualWeatherChange",
                "OnNetworkPlayerCreativeFly",
                "OnNetworkCreatureDied",
                "OnNetworkHealthFallDamage",
                "OnNetworkHealthHealUpdate",
                "OnNetworkHealthInjured",
                "OnNetworkHumanModelRow",
                "OnNetworkInventorySlotChanged",
                "OnNetworkClothingSlotChanged",
                "OnNetworkLevelExperience",
                "OnNetworkSetOnFire",
                "OnNetworkParticleSystemAdded",
                "OnNetworkPickableAttract",
                "OnNetworkActiveSlotChanged",
                "OnNetworkPlayerAim",
                "OnNetworkPlayerDig",
                "OnNetworkPlayerHit",
                "OnNetworkPlayerInteract",
                "OnNetworkPlayerDrop",
                "OnNetworkRandomFeedBehaviorChanged",
                "OnNetworkRandomPeckBehaviorChanged",
                "OnNetworkRiderDismounting",
                "OnNetworkRiderMounting",
                "OnNetworkSicknessChanged",
                "OnNetworkNauseaEffect",
                "OnNetworkStubbornSteedHeadShakeOrder",
                "OnNetworkVitalStatsUpdate",
                "OnNetworkAdjustableDelayGateBlockEdited",
                "OnNetworkAdjustableDelayGateItemEdited",
                "OnNetworkBatteryBlockVoltageEdited",
                "OnNetworkBatteryItemVoltageEdited",
                "OnNetworkButtonBlockVoltageEdited",
                "OnNetworkButtonItemVoltageEdited",
                "OnNetworkElectricityPersistentVoltageChanged",
                "OnNetworkOpenInventoryRequest",
                "OnNetworkExplosionCell",
                "OnNetworkExplosionSound",
                "OnNetworkFireBlockAdded",
                "OnNetworkFireBlockRemoved",
                "OnNetworkFurnacesUpdate",
                "OnNetworkTimeUpdate",
                "OnNetworkMemoryBankBlockEdited",
                "OnNetworkMemoryBankItemEdited",
                "OnNetworkMemoryBankDataHarvested",
                "OnNetworkMovingBlockAdded",
                "OnNetworkMovingBlockRemoved",
                "OnNetworkPickableCreated",
                "OnNetworkPickableDeleted",
                "OnNetworkPickableRequestSync",
                "OnNetworkPistonBlockEdited",
                "OnNetworkPistonItemEdited",
                "OnNetworkProjectileFired",
                "OnNetworkSignEditRequested",
                "OnNetworkSignDataChanged",
                "OnNetworkSkyLightning",
                "OnNetworkSwitchBlockVoltageEdited",
                "OnNetworkSwitchItemVoltageEdited",
                "OnNetworkTerrainCellChanged",
                "OnNetworkTruthTableBlockEdited",
                "OnNetworkTruthTableItemEdited",
                "OnNetworkTruthTableDataHarvested",
                "OnNetworkFogChanged",
                "OnNetworkPrecipitationChanged",
                "OnNetworkTerrainSyncChunkList",
                "OnNetworkPlayerRestart",
                "OnNetworkPlayerIntoPlaying",
                "OnNetworkPlayerCreate",
                "OnNetworkPlayerModify",
                "OnNetworkPlayerStateChanged",
                "OnNetworkCrossbowPull",
                "OnNetworkDispenserClicked",
                "OnNetworkDragDropItem",
                "OnNetworkMoveItem",
                "OnNetworkPlayerDragDrop",
                "OnNetworkChokeRequest",
                "OnNetworkRequestSkin",
                "OnNetworkPlayerPlaying",
                "OnNetworkSleep",
                "OnNetworkWakeUp",
                "OnNetworkPlayerBodyUpdate",
                "OnNetworkTerrainPlayerLocationUpdate",
                "OnNetworkBodyUpdate",
                "OnNetworkBodyRemoved",
                "OnNetworkBodyUpdated",
                "OnNetworkEditableItemDataHarvested"
            ];
            foreach (string hookName in hookNames) {
                ModsManager.RegisterHook(hookName, this);
            }

            NetworkManager.InitializeAction = () => {
                PacketManager.Initialize();
            };
            NetworkManager.UpdateAction = () => {
                (NetworkManager.NetworkServer as NetworkServer)?.Update();
                (NetworkManager.NetworkClient as NetworkClient)?.Update();
                (NetworkManager.RelayHost as RelayHost)?.Update();
                (NetworkManager.RelayClient as RelayClient)?.Update();
                PacketManager.PackageFragment.CleanupStates();
            };
            NetworkManager.StopAction = () => {
                (NetworkManager.NetworkServer as NetworkServer)?.Stop();
                (NetworkManager.NetworkClient as NetworkClient)?.Disconnect();
                (NetworkManager.RelayHost as RelayHost)?.Disconnect();
                (NetworkManager.RelayClient as RelayClient)?.Disconnect();
                NetworkManager.NetworkServer = null;
                NetworkManager.NetworkClient = null;
                NetworkManager.RelayHost = null;
                NetworkManager.RelayClient = null;
                NetworkBodyInterpolation.Clear();
                PlayerBodyUpdatePacket.ClearSnapshots();
            };
            NetworkManager.IsServerRunningFunc = () =>
                ((NetworkManager.NetworkServer as NetworkServer)?.IsRunning ?? false) || ((NetworkManager.RelayHost as RelayHost)?.IsConnected ?? false);
            NetworkManager.IsClientRunningFunc = () =>
                ((NetworkManager.NetworkClient as NetworkClient)?.IsConnected ?? false) || ((NetworkManager.RelayClient as RelayClient)?.IsConnected ?? false);
            NetworkManager.ClientLatencyFunc = () =>
                NetworkManager.IsClientRunning ? LatencyProbe.Latency : -1;
            NetworkManager.MySessionFunc = () =>
                (NetworkManager.NetworkClient as NetworkClient)?.MySession ?? (NetworkManager.RelayClient as RelayClient)?.MySession;
            NetworkManager.ServerSessionsFunc = () =>
                ((NetworkManager.NetworkServer as NetworkServer)?.ClientSessions.Values as IEnumerable<ClientSession> ?? [])
                .Concat((NetworkManager.RelayHost as RelayHost)?.Sessions ?? []);
            NetworkManager.RelayGlobalCodeFunc = () =>
                (NetworkManager.RelayHost as RelayHost)?.GlobalCode;
            NetworkManager.RelayRoomCodeFunc = () =>
                (NetworkManager.RelayHost as RelayHost)?.RoomCode;
            NetworkManager.ResolveRelayJoinInfoAsyncFunc = (globalCode) =>
                CenterServerClient.ResolveRelayJoinInfoAsync(globalCode);
            NetworkManager.CreateTerrainSerializerNetFunc = () =>
                new TerrainSerializerNet();
            NetworkManager.SavePlayerEntityAction = (player, networkGuid) =>
                player.Project?.FindSubsystem<SubsystemNetwork>()?.SavePlayerEntity(player, networkGuid);

            NetworkManager.StartServerAction = () => {
                var server = new NetworkServer();
                NetworkManager.NetworkServer = server;
                server.Start();
            };
            NetworkManager.ConnectServerAction = (endPoint) => {
                var client = new NetworkClient();
                NetworkManager.NetworkClient = client;
                client.Connect(endPoint);
            };
            NetworkManager.StartRelayHostAsyncFunc = async () => {
                try {
                    NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Creating;
                    var endPoint = await CenterServerClient.GetBestRelayEndPointAsync();
                    var host = new RelayHost();
                    NetworkManager.RelayHost = host;
                    host.Start(endPoint);
                }
                catch (Exception ex) {
                    Engine.Log.Warning($"Failed to start relay host: {ex.Message}");
                    NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Failed;
                }
            };
            NetworkManager.StartRelayHostAction = (relayEndPoint) => {
                var host = new RelayHost();
                NetworkManager.RelayHost = host;
                host.Start(relayEndPoint);
            };
            NetworkManager.ConnectRelayAction = (relayEndPoint, roomCode) => {
                var client = new RelayClient(roomCode);
                NetworkManager.RelayClient = client;
                client.Connect(relayEndPoint);
            };
            NetworkManager.QueueAction = (packetObj) => {
                if (packetObj is Packet packet) {
                    (NetworkManager.NetworkClient as NetworkClient)?.Send(packet);
                    (NetworkManager.NetworkServer as NetworkServer)?.Board(packet);
                    (NetworkManager.RelayClient as RelayClient)?.Send(packet);
                    (NetworkManager.RelayHost as RelayHost)?.Board(packet);
                }
            };
            NetworkManager.QueuePickableCreateAction = (pickable) => NetworkManager.Queue(new PickableCreatePacket(pickable));
            NetworkManager.QueuePickableUpdateAction = (pickables) => NetworkManager.Queue(new PickableUpdatePacket(pickables));

            NetworkManager.Initialize();
        }

        public override void OnXdbLoad(XElement xElement) {
            base.OnXdbLoad(xElement);

            if (ModsManager.FindElementByGuid(xElement, "85023bf8-1c90-4dd1-9442-e6c13691d078", out XElement projectTemplate)) {
                if (ModsManager.FindElement(projectTemplate, e => e.Name.LocalName == "MemberSubsystemTemplate" && e.Attribute("Name")?.Value == "Network", out _)) {
                    return;
                }

                string networkTemplateGuid = Guid.NewGuid().ToString();

                projectTemplate.Add(new XElement("MemberSubsystemTemplate",
                    new XAttribute("Name", "Network"),
                    new XAttribute("Guid", Guid.NewGuid().ToString()),
                    new XAttribute("InheritanceParent", networkTemplateGuid)));

                XElement subsystemTemplate = new XElement("SubsystemTemplate",
                    new XAttribute("Name", "Network"),
                    new XAttribute("Guid", networkTemplateGuid),
                    new XAttribute("InheritanceParent", "a3914c13-7801-46a6-b23a-9e2b36e9defb"),
                    new XElement("Parameter",
                        new XAttribute("Name", "Class"),
                        new XAttribute("Guid", Guid.NewGuid().ToString()),
                        new XAttribute("Value", "Game.Network.SubsystemNetwork"),
                        new XAttribute("Type", "string")));

                projectTemplate.Parent?.Add(subsystemTemplate);
            }
        }
    public override bool IsNetworkMode(ComponentBody body) => NetworkBodySyncHelpers.ShouldBeNetworkControlled(body);

    public override void OnEntityAdd(Entity entity) {
        if (!NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new EntityLoadPacket(entity));
        }
    }

    public override void OnEntityRemove(Entity entity) {
        if (!NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new EntityRemovePacket(entity.Id));
        }
    }

    public override void OnNetworkChaseBehaviorAttackChanged(ComponentChaseBehavior behavior, bool isAttack) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ChaseBehaviorPacket(behavior, isAttack));
    }

    public override void OnNetworkCreatureSound(ComponentCreatureSounds sounds, byte type, bool skipIfRecentlyPlayed) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed));
    }

    public override void OnNetworkDamage(ComponentDamage damage) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new DamageSyncPacket(damage));
    }

    public override void OnNetworkDigInMudBehaviorChanged(ComponentDigInMudBehavior behavior, bool isDigIn) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new DigInMudBehaviorPacket(behavior, isDigIn));
    }

    public override void OnNetworkEatPickableBehaviorChanged(ComponentEatPickableBehavior behavior, bool isFeed) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new EatPickableBehaviorPacket(behavior, isFeed));
    }

    public override void OnNetworkFishOutOfWaterBehaviorChanged(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new FishOutOfWaterBehaviorPacket(behavior, isBend));
    }

    public override void OnNetworkLookAroundBehaviorChanged(ComponentLookAroundBehavior behavior, bool isLookRandom) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new LookAroundBehaviorPacket(behavior, isLookRandom));
    }

    public override void OnNetworkFluEffect(ComponentFlu flu) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.FluEffect));
    }

    public override void OnNetworkFluUpdate(ComponentFlu flu) {
        if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(1.0, 0.5))
            NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat));
    }

    public override void OnNetworkFluSneeze(ComponentFlu flu) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.Sneeze));
    }

    public override void OnNetworkFluStarted(ComponentFlu flu) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.StartFlu));
    }

    public override void OnNetworkFluImpulse(ComponentFlu flu, Vector3 impulse) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ComponentFluPacket(flu, impulse));
    }

    public override void OnNetworkFurnaceUpdate(ComponentFurnace furnace) {
        if (NetworkManager.IsServerRunning && Time.PeriodicEvent(2.0, 0.0))
            NetworkManager.Queue(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
    }

    public override void OnNetworkManualLightingStrike(ComponentGui gui, Vector3 position, Vector3 direction) {
        NetworkManager.Queue(new SubsystemSkyPacket(position, direction));
    }

    public override void OnNetworkManualTimeChange(ComponentGui gui, double totalElapsedGameTime, double timeOfDayOffset) {
        NetworkManager.Queue(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
    }

    public override void OnNetworkManualWeatherChange(ComponentGui gui, int type) {
        NetworkManager.Queue(new SubsystemWeatherPacket(type));
    }

    public override void OnNetworkPlayerCreativeFly(ComponentGui gui, ComponentPlayer player) {
        NetworkManager.Queue(new PlayerCreativeFlyPacket(player));
    }

    public override void OnNetworkCreatureDied(ComponentHealth health) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new HealthSyncPacket(health));
    }

    public override void OnNetworkHealthFallDamage(ComponentHealth health, float fallDamage, string cause) {
        InjureRequestPacket.SendFallDamage(health, fallDamage, cause);
    }

    public override void OnNetworkHealthHealUpdate(ComponentHealth health) {
        if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(2.0, 0.0))
            NetworkManager.Queue(new HealthSyncPacket(health));
    }

    public override void OnNetworkHealthInjured(ComponentHealth health, Injury injury, float amount) {
        if (!NetworkManager.IsClientRunning && amount > 0f)
            NetworkManager.Queue(new InjureEventPacket(health, injury.Attacker, amount, injury.Cause, injury.IgnoreInvulnerability));
    }

    public override void OnNetworkHumanModelRow(ComponentHumanModel model) {
        NetworkManager.Queue(new HumanRowBehaviorPacket(model));
    }

    public override void OnNetworkInventorySlotChanged(IInventory inventory, int slotIndex) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new InventorySyncPacket(inventory));
    }

    public override void OnNetworkClothingSlotChanged(IInventory inventory, int slotIndex) {
        if (!NetworkManager.IsClientRunning && inventory is ComponentClothing clothing && clothing.Id > 0)
            NetworkManager.Queue(new ClothingSyncPacket(clothing));
    }

    public override void OnNetworkLevelExperience(ComponentLevel level, int count, bool playSound) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new PlayerAddExperiencePacket(level.m_componentPlayer.PlayerData, count, playSound, level.m_componentPlayer.PlayerData.Level));
    }

    public override void OnNetworkSetOnFire(ComponentOnFire onFire, ComponentCreature attacker, float duration) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new EntityOnFirePacket(onFire, attacker, duration));
    }

    public override void OnNetworkParticleSystemAdded(ParticleSystemBase particleSystem) {
        if (NetworkManager.IsClientRunning || particleSystem is not HitValueParticleSystem hitValueParticleSystem)
            return;

        HitValueParticleSystem.Particle particle = hitValueParticleSystem.Particles[0];
        NetworkManager.Queue(new HitValuePacket(
            particle.Position,
            particle.Velocity,
            particle.BaseColor,
            particle.Text,
            true,
            hitValueParticleSystem.AttackerPlayerIndex));
    }

    public override void OnNetworkPickableAttract(Pickable pickable) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new PickableSetFlyToPositionPacket(pickable));
    }

    public override void OnNetworkActiveSlotChanged(ComponentPlayer player) {
        if (player.ComponentMiner?.Inventory != null)
            NetworkManager.Queue(new ActiveSlotChangePacket(player.ComponentMiner.Inventory));
    }

    public override void OnNetworkPlayerAim(ComponentPlayer player) {
        NetworkManager.Queue(new PlayerAimPacket(player));
    }

    public override void OnNetworkPlayerDig(ComponentPlayer player) {
        NetworkManager.Queue(new PlayerDigPacket(player));
    }

    public override void OnNetworkPlayerHit(ComponentPlayer player, ComponentBody hitBody, Vector3 hitPoint, Vector3 direction) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new PlayerHitPacket(player, hitBody, hitPoint, direction));
    }

    public override void OnNetworkPlayerInteract(ComponentPlayer player) {
        NetworkManager.Queue(new PlayerInteractPacket(player));
    }

    public override void OnNetworkPlayerDrop(ComponentPlayer player) {
        NetworkManager.Queue(new PlayerDropPacket(player));
    }

    public override void OnNetworkRandomFeedBehaviorChanged(ComponentRandomFeedBehavior behavior, bool isFeed) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new RandomFeedBehaviorPacket(behavior, isFeed));
    }

    public override void OnNetworkRandomPeckBehaviorChanged(ComponentRandomPeckBehavior behavior, bool isPeck) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new RandomPeckBehaviorPacket(behavior, isPeck));
    }

    public override void OnNetworkRiderDismounting(ComponentRider rider, bool isRequest) {
        NetworkManager.Queue(new ComponentMountPacket(rider, isRequest));
    }

    public override void OnNetworkRiderMounting(ComponentRider rider, ComponentMount mount, bool isRequest) {
        NetworkManager.Queue(new ComponentMountPacket(rider, mount, isRequest));
    }

    public override void OnNetworkSicknessChanged(ComponentSickness sickness) {
        if (NetworkManager.IsServerRunning)
            NetworkManager.Queue(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.SyncStat));
    }

    public override void OnNetworkNauseaEffect(ComponentSickness sickness) {
        if (NetworkManager.IsServerRunning)
            NetworkManager.Queue(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.NauseaEffect));
    }

    public override void OnNetworkStubbornSteedHeadShakeOrder(ComponentStubbornSteedBehavior behavior, float headShakeOrder) {
        if (NetworkManager.IsServerRunning)
            NetworkManager.Queue(new StubbornSteedBehaviorPacket(behavior, headShakeOrder));
    }

    public override void OnNetworkVitalStatsUpdate(ComponentVitalStats stats) {
        if (NetworkManager.IsServerRunning)
            NetworkManager.Queue(new VitalStatSyncPacket(stats));
    }

    public override void OnNetworkAdjustableDelayGateBlockEdited(SubsystemAdjustableDelayGateBlockBehavior subsystem, CellFace cell, int newDelay) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new AdjustableDelayGatePacket(cell, false, default, default, newDelay));
    }

    public override void OnNetworkAdjustableDelayGateItemEdited(SubsystemAdjustableDelayGateBlockBehavior subsystem, int inventoryId, int slotIndex, int newDelay) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new AdjustableDelayGatePacket(default, true, inventoryId, slotIndex, newDelay));
    }

    public override void OnNetworkBatteryBlockVoltageEdited(SubsystemBatteryBlockBehavior subsystem, CellFace cell, int newVoltageLevel) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel));
    }

    public override void OnNetworkBatteryItemVoltageEdited(SubsystemBatteryBlockBehavior subsystem, int inventoryId, int slotIndex, int newVoltageLevel) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new BatteryVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
    }

    public override void OnNetworkButtonBlockVoltageEdited(SubsystemButtonBlockBehavior subsystem, CellFace cell, int newVoltageLevel) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ButtonVoltagePacket(cell, false, default, default, newVoltageLevel));
    }

    public override void OnNetworkButtonItemVoltageEdited(SubsystemButtonBlockBehavior subsystem, int inventoryId, int slotIndex, int newVoltageLevel) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new ButtonVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
    }

    public override void OnNetworkElectricityPersistentVoltageChanged(SubsystemElectricity subsystem, List<SubsystemElectricity.NetSimulate> list) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SubsystemElectricityPacket(list));
    }

    public override void OnNetworkOpenInventoryRequest(SubsystemEntityBlockBehavior subsystem, Point3 point) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new OpenInventoryByPointPacket(point));
    }

    public override void OnNetworkExplosionCell(Dictionary<Point2, List<(Point3, float)>> explosionCells) {
        NetworkManager.Queue(new ExplosionCellPacket(explosionCells));
    }

    public override void OnNetworkExplosionSound(Vector3 position, float pressure, float delay) {
        NetworkManager.Queue(new ExplosionSoundPacket(position, pressure, delay));
    }

    public override void OnNetworkFireBlockAdded(int x, int y, int z, float expandability) {
        NetworkManager.Queue(new FireBlockAddPacket(x, y, z, expandability));
    }

    public override void OnNetworkFireBlockRemoved(int x, int y, int z) {
        NetworkManager.Queue(new FireBlockRemovePacket(x, y, z));
    }

    public override void OnNetworkFurnacesUpdate(SubsystemFurnaceBlockBehavior subsystem, List<ComponentFurnace> furnaces) {
        if (NetworkManager.IsServerRunning)
            NetworkManager.Queue(new ComponentFurnacePacket(furnaces));
    }

    public override void OnNetworkTimeUpdate(double totalElapsedGameTime, double timeOfDayOffset) {
        NetworkManager.Queue(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
    }

    public override void OnNetworkMemoryBankBlockEdited(SubsystemMemoryBankBlockBehavior subsystem, CellFace cell, MemoryBankData data) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new MemoryBankDataPacket(cell, false, default, default, data));
    }

    public override void OnNetworkMemoryBankItemEdited(SubsystemMemoryBankBlockBehavior subsystem, int inventoryId, int slotIndex, MemoryBankData data) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new MemoryBankDataPacket(default, true, inventoryId, slotIndex, data));
    }

    public override void OnNetworkMemoryBankDataHarvested(int id, MemoryBankData data) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new MemoryBankDataPacket(id, data));
    }

    public override void OnNetworkMovingBlockAdded(IMovingBlockSet movingBlockSet) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
    }

    public override void OnNetworkMovingBlockRemoved(IMovingBlockSet movingBlockSet, bool isCrash) {
        NetworkManager.Queue(new MovingBlockRemovePacket(movingBlockSet, isCrash));
    }

    public override void OnNetworkPickableCreated(Pickable pickable) {
        NetworkManager.Queue(new PickableCreatePacket(pickable));
    }

    public override void OnNetworkPickableDeleted(Pickable pickable) {
        NetworkManager.Queue(new PickableDeletePacket(pickable));
    }

    public override void OnNetworkPickableRequestSync(int id) {
        NetworkManager.Queue(new PickableRequestSyncPacket(id));
    }

    public override void OnNetworkPistonBlockEdited(SubsystemPistonBlockBehavior subsystem, CellFace cell, int data) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new PistonDataPacket(cell, false, default, default, data));
    }

    public override void OnNetworkPistonItemEdited(SubsystemPistonBlockBehavior subsystem, int inventoryId, int slotIndex, int data) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new PistonDataPacket(default, true, inventoryId, slotIndex, data));
    }

    public override void OnNetworkProjectileFired(Projectile projectile, bool isFireProjectile) {
        NetworkManager.Queue(new ProjectilePacket(projectile, isFireProjectile));
    }

    public override void OnNetworkSignEditRequested(SubsystemSignBlockBehavior subsystem, Point3 point) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new EditSignPacket(point));
    }

    public override void OnNetworkSignDataChanged(SubsystemSignBlockBehavior subsystem, Point3 point, string[] lines, Color[] colors, string url) {
        NetworkManager.Queue(new SignBlockPacket(point, lines, colors, url));
    }

    public override void OnNetworkSkyLightning(Vector3 targetPosition) {
        NetworkManager.Queue(new SubsystemSkyPacket(targetPosition));
    }

    public override void OnNetworkSwitchBlockVoltageEdited(SubsystemSwitchBlockBehavior subsystem, CellFace cell, int newVoltageLevel) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SwitchVoltagePacket(cell, false, default, default, newVoltageLevel));
    }

    public override void OnNetworkSwitchItemVoltageEdited(SubsystemSwitchBlockBehavior subsystem, int inventoryId, int slotIndex, int newVoltageLevel) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SwitchVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
    }

    public override void OnNetworkTerrainCellChanged(SubsystemTerrain subsystem, int x, int y, int z, int value) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new TerrainChangeCellPacket(x, y, z, value));
    }

    public override void OnNetworkTruthTableBlockEdited(SubsystemTruthTableCircuitBlockBehavior subsystem, CellFace cell, TruthTableData data) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new TruthTableDataPacket(cell, false, default, default, data));
    }

    public override void OnNetworkTruthTableItemEdited(SubsystemTruthTableCircuitBlockBehavior subsystem, int inventoryId, int slotIndex, TruthTableData data) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new TruthTableDataPacket(default, true, inventoryId, slotIndex, data));
    }

    public override void OnNetworkTruthTableDataHarvested(int id, TruthTableData data) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new TruthTableDataPacket(id, data));
    }

    public override void OnNetworkFogChanged(double startTime, float rampTime, double endTime) {
        NetworkManager.Queue(new SubsystemWeatherPacket(startTime, rampTime, endTime));
    }

    public override void OnNetworkPrecipitationChanged(double startTime, double endTime, float intensity, float rampTime) {
        NetworkManager.Queue(new SubsystemWeatherPacket(startTime, endTime, intensity, rampTime));
    }

    public override void OnNetworkTerrainSyncChunkList(TerrainUpdater updater, int playerIndex, List<TerrainChunk> chunks) {
        NetworkManager.Queue(new TerrainSyncChunkListPacket(chunks) {
            To = Enumerable.FirstOrDefault(NetworkManager.ServerSessions, c => c.PlayerIndex == playerIndex)
        });
    }

    public override void OnNetworkPlayerRestart(PlayerData playerData) {
        if (NetworkManager.IsClientRunning && playerData.IsMainPlayer)
            NetworkManager.Queue(new PlayerRestartPacket(playerData));
    }

    public override void OnNetworkPlayerIntoPlaying() {
        ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>()?.ComponentPlayers.FirstOrDefault(
            p => p.PlayerData.PlayerIndex == NetworkManager.PlayerIndex);
        NetworkManager.Queue(new PlayerIntoPlayingPacket(mainPlayer));
    }

    public override void OnNetworkPlayerCreate(PlayerData playerData) {
        NetworkManager.Queue(new PlayerCreatePacket(playerData));
    }

    public override void OnNetworkPlayerModify(PlayerData playerData) {
        NetworkManager.Queue(new PlayerModifyPacket(playerData));
    }

    public override void OnNetworkPlayerStateChanged(NetworkState state) {
        NetworkManager.SetState(state);
        NetworkManager.Queue(new StatePacket(NetworkManager.PlayerIndex, state));
    }

    public override void OnNetworkCrossbowPull(IInventory inventory, int slotIndex) {
        NetworkManager.Queue(new CrossbowPullPacket(inventory, slotIndex));
    }

    public override void OnNetworkDispenserClicked(Point3 position, byte data) {
        NetworkManager.Queue(new DispenserPacket(position, data));
    }

    public override void OnNetworkDragDropItem(IInventory sourceInventory, int sourceSlotIndex, DragMode dragMode, IInventory targetInventory, int targetSlotIndex, bool processingOnly, int dragCount) {
        NetworkManager.Queue(new DragDropItemPacket(sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly, dragCount));
    }

    public override void OnNetworkMoveItem(IInventory sourceInventory, int sourceSlotIndex, IInventory targetInventory, int targetSlotIndex, int count) {
        NetworkManager.Queue(new MoveItemPacket(sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count));
    }

    public override void OnNetworkPlayerDragDrop(PlayerData playerData, int inventoryId, int slotIndex, Vector3 worldPos, int count) {
        NetworkManager.Queue(new PlayerDragDropPacket(playerData, inventoryId, slotIndex, worldPos, count));
    }

    public override void OnNetworkChokeRequest(ComponentHealth health, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability) {
        NetworkManager.Queue(new InjureRequestPacket(health, attacker, amount, cause, ignoreInvulnerability, InjureRequestPacket.RequestInjureType.Choke));
    }

    public override void OnNetworkRequestSkin(ComponentClothing clothing, string skinName) {
        if (Time.PeriodicEvent(1.0, 0.0) && NetworkManager.IsClientRunning)
            NetworkManager.Queue(new RequestSkinPacket(skinName));
        if (Time.PeriodicEvent(1.0, 0.0) && !NetworkManager.IsClientRunning)
            NetworkManager.Queue(new WhoHasSkinPacket(skinName));
    }

    public override void OnNetworkPlayerPlaying(PlayerData playerData) {
        NetworkManager.SetState(NetworkState.Playing);
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new StatePacket(NetworkManager.PlayerIndex, NetworkState.Playing));
    }

    public override void OnNetworkSleep(ComponentSleep sleep, bool allowManualWakeup) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SleepRequestPacket(sleep, allowManualWakeup));
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SleepStatusPacket(sleep, allowManualWakeup, true));
    }

    public override void OnNetworkWakeUp(ComponentSleep sleep) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new WakeupRequestPacket(sleep));
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new WakeupStatusPacket(sleep));
    }

    public override void OnNetworkPlayerBodyUpdate(ComponentPlayer player, bool isMainPlayer) {
        if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(0.2f, 0.0)) {
            foreach (ClientSession session in NetworkManager.ServerSessions) {
                if (session.NetworkState < NetworkState.Playing || session.PlayerIndex == player.PlayerData.PlayerIndex)
                    continue;
                NetworkManager.Queue(new PlayerBodyUpdatePacket(player, session) { To = session });
            }
        }
        if (isMainPlayer && Time.PeriodicEvent(0.2f, 0.0))
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
    }

    public override void OnNetworkTerrainPlayerLocationUpdate(TerrainUpdater updater, TerrainUpdater.UpdateLocation location) {
        if (NetworkManager.IsClientRunning && NetworkManager.PlayerIndex >= 0)
            NetworkManager.Queue(new TerrainViewDistancePacket(location));
    }

    public override void OnNetworkBodyUpdate(ComponentBody body, float dt) {
        NetworkBodyInterpolation.Update(body, dt);
    }

    public override void OnNetworkBodyRemoved(ComponentBody body) {
        NetworkBodyInterpolation.RemoveTarget(body);
    }

    public override void OnNetworkBodyUpdated(ComponentBody body, float dt) {
        BehaviorSyncPacket.CollectBehaviorForSync(body, dt);
    }

    public override void OnNetworkEditableItemDataHarvested<T>(int id, T data) {
        if (data is MemoryBankData memoryBankData)
            OnNetworkMemoryBankDataHarvested(id, memoryBankData);
        else if (data is TruthTableData truthTableData)
            OnNetworkTruthTableDataHarvested(id, truthTableData);
    }

    }
}
