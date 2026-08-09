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
                "OnMinerDig",
                "OnBlockDug",
                "OnEntityAdd",
                "OnEntityRemove",
                "OnNetworkChaseBehaviorAttackChanged",
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
                "OnNetworkExtinguish",
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
                "OnNetworkExplosion",
                "OnNetworkFireBlockAdded",
                "OnNetworkFireBlockRemoved",
                "OnNetworkFurnacesUpdate",
                "OnNetworkTimeUpdate",
                "OnNetworkMemoryBankBlockEdited",
                "OnNetworkMemoryBankItemEdited",
                "OnNetworkMemoryBankDataHarvested",
                "OnNetworkMovingBlockAdded",
                "OnNetworkMovingBlockRemoved",
                "OnNetworkFurnitureHammerRequest",
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
                NetworkSessionLifecycleTracker.Reset();
                ApplicationReliabilityRuntime.Reset();
            };
            NetworkManager.UpdateAction = () => {
                NetworkFaultInjector.Update();
                (NetworkManager.NetworkServer as NetworkServer)?.Update();
                (NetworkManager.NetworkClient as NetworkClient)?.Update();
                (NetworkManager.RelayHost as RelayHost)?.Update();
                (NetworkManager.RelayClient as RelayClient)?.Update();
                PacketManager.PackageFragment.CleanupStates();
                NetworkCompatibilityRuntime.UpdateClientHandshake(NetworkManager.MySession);
                NetworkSessionAdmissionRuntime.UpdateClientAdmission(NetworkManager.MySession);
                ApplicationReliabilityRuntime.Update();
                if (NetworkCompatibilityRuntime.TryTakePendingClientRejection(
                        out NetworkCompatibilityResult rejection)) {
                    NetworkManager.StopClient();
                    GameManager.DisposeProject();
                    ScreensManager.SwitchScreen("MainMenu");
                    DialogsManager.Alert(
                        "Incompatible multiplayer",
                        rejection.ToDisplayMessage());
                }
                else if (NetworkSessionAdmissionRuntime.TryTakePendingClientRejection(
                        out NetworkSessionAdmissionResult admissionRejection)) {
                    NetworkManager.StopClient();
                    GameManager.DisposeProject();
                    ScreensManager.SwitchScreen("MainMenu");
                    DialogsManager.Alert(
                        "Multiplayer session rejected",
                        admissionRejection.ToDisplayMessage());
                }
                NetworkDigConfirmationRuntime.Tick();
                QueuePeriodicPlayerStats();
            };
            NetworkManager.StopClientAction = () => {
                ModNetworkManager.ClearSession(NetworkManager.MySession);
                NetworkDigConfirmationRuntime.ClearClientState();
                ApplicationReliabilityRuntime.ClearClient();
                CreativeFlyResultTracker.Reset();
                (NetworkManager.NetworkClient as NetworkClient)?.Disconnect();
                (NetworkManager.RelayClient as RelayClient)?.Disconnect();
                NetworkManager.NetworkClient = null;
                NetworkManager.RelayClient = null;
                LatencyProbe.Reset();
            };
            NetworkManager.StopAction = () => {
                (NetworkManager.NetworkServer as NetworkServer)?.Stop();
                (NetworkManager.RelayHost as RelayHost)?.Disconnect();
                NetworkManager.NetworkServer = null;
                NetworkManager.RelayHost = null;
                NetworkManager.StopClient();
                NetworkBodyInterpolation.Clear();
                BehaviorSyncPacket.Clear();
                PlayerBodyUpdatePacket.ClearSnapshots();
                NetworkFaultInjector.ClearPending();
                ApplicationReliabilityRuntime.Reset();
                FurnitureHammerPacket.ClearState();
                NetworkDigConfirmationRuntime.Reset();
                PlayerPacketBase.ClearHitThrottleState();
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
            NetworkManager.RequestPlayerEntityAction = playerIndex =>
                NetworkManager.Queue(new PlayerEntityRequestPacket(playerIndex));
            NetworkManager.DisconnectSessionAction = (session, reason) => {
                if ((NetworkManager.NetworkServer as NetworkServer)?.DisconnectSession(
                        session,
                        reason) == true) {
                    return;
                }
                (NetworkManager.RelayHost as RelayHost)?.DisconnectSession(session, reason);
            };

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
                NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Creating;
                try {
                    var host = new RelayHost();
                    NetworkManager.RelayHost = host;
                    host.Start(relayEndPoint);
                }
                catch {
                    NetworkManager.RelayHost = null;
                    NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Failed;
                    throw;
                }
            };
            NetworkManager.ConnectRelayAction = (relayEndPoint, roomCode) => {
                var client = new RelayClient(roomCode);
                NetworkManager.RelayClient = client;
                client.Connect(relayEndPoint);
            };
            NetworkManager.QueueAction = (packetObj) => {
                if (packetObj is Packet packet) {
                    NetworkPacketTrafficTracker.RecordQueued(packet);
                    (NetworkManager.NetworkClient as NetworkClient)?.Send(packet);
                    (NetworkManager.NetworkServer as NetworkServer)?.Board(packet);
                    (NetworkManager.RelayClient as RelayClient)?.Send(packet);
                    (NetworkManager.RelayHost as RelayHost)?.Board(packet);
                }
            };
            NetworkManager.SendModMessageToServerAction = envelope => {
                ModMessagePacket packet = new(envelope);
                (NetworkManager.NetworkClient as NetworkClient)?.Send(packet);
                (NetworkManager.RelayClient as RelayClient)?.Send(packet);
            };
            NetworkManager.SendModMessageToClientAction = (envelope, session) => {
                ModMessagePacket packet = new(envelope) { To = session };
                (NetworkManager.NetworkServer as NetworkServer)?.Board(packet);
                (NetworkManager.RelayHost as RelayHost)?.Board(packet);
            };
            NetworkManager.BroadcastModMessageAction = (envelope, except) => {
                ModMessagePacket packet = new(envelope) { Except = except };
                (NetworkManager.NetworkServer as NetworkServer)?.Board(packet);
                (NetworkManager.RelayHost as RelayHost)?.Board(packet);
            };
            NetworkManager.QueuePickableCreateAction = pickable => {
                long revision = PickableLifecycleTracker.GetAuthoritativeRevision(
                    GameManager.Project,
                    pickable.Id);
                NetworkManager.Queue(new PickableCreatePacket(pickable, revision));
            };
            NetworkManager.QueuePickableUpdateAction = pickables => {
                Project project = GameManager.Project;
                List<PickableLifecycleStamp> stamps = pickables
                    .Where(pickable => pickable.Id > 0)
                    .Select(pickable => new PickableLifecycleStamp {
                        PickableId = pickable.Id,
                        Revision = PickableLifecycleTracker.AdvanceAuthoritativeRevision(
                            project,
                            pickable.Id)
                    })
                    .ToList();
                NetworkManager.Queue(new PickableUpdatePacket(pickables, stamps));
            };

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
        if (!NetworkManager.IsClientRunning && entity.Id > 0) {
            EntityLifecycleTracker.AdvanceAuthoritativeRevision(
                entity.Project,
                entity.Id);
            NetworkManager.Queue(new EntityLoadPacket(entity));
            foreach (Packet state in NetworkEntityStateBaselineFactory.Create(entity)) {
                NetworkManager.Queue(state);
            }
        }
    }

    public override void OnEntityRemove(Entity entity) {
        if (!NetworkManager.IsClientRunning && entity.Id > 0) {
            long revision = EntityLifecycleTracker.AdvanceAuthoritativeRevision(
                entity.Project,
                entity.Id);
            NetworkManager.Queue(new EntityRemovePacket(entity.Id, revision));
        }
    }

    public override void OnNetworkChaseBehaviorAttackChanged(ComponentChaseBehavior behavior, bool isAttack) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureChase,
                behavior.Entity.Id);
            NetworkManager.Queue(new ChaseBehaviorPacket(behavior, isAttack, revision));
        }
    }

    public override void OnNetworkDamage(ComponentDamage damage) {
        if (!NetworkManager.IsClientRunning && damage.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                damage.Entity.Project,
                NetworkStateRevisionDomain.Damage,
                damage.Entity.Id);
            NetworkManager.Queue(new DamageSyncPacket(damage, revision));
        }
    }

    public override void OnNetworkDigInMudBehaviorChanged(ComponentDigInMudBehavior behavior, bool isDigIn) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureDigInMud,
                behavior.Entity.Id);
            NetworkManager.Queue(new DigInMudBehaviorPacket(behavior, isDigIn, revision));
        }
    }

    public override void OnNetworkEatPickableBehaviorChanged(ComponentEatPickableBehavior behavior, bool isFeed) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureEatPickable,
                behavior.Entity.Id);
            NetworkManager.Queue(new EatPickableBehaviorPacket(behavior, isFeed, revision));
        }
    }

    public override void OnNetworkFishOutOfWaterBehaviorChanged(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureFishOutOfWater,
                behavior.Entity.Id);
            NetworkManager.Queue(new FishOutOfWaterBehaviorPacket(behavior, isBend, revision));
        }
    }

    public override void OnNetworkLookAroundBehaviorChanged(ComponentLookAroundBehavior behavior, bool isLookRandom) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureLookAround,
                behavior.Entity.Id);
            NetworkManager.Queue(new LookAroundBehaviorPacket(behavior, isLookRandom, revision));
        }
    }

    public override void OnNetworkFluEffect(ComponentFlu flu) {
        QueueFluState(flu, ComponentFluPacket.EventType.FluEffect);
    }

    public override void OnNetworkFluUpdate(ComponentFlu flu) {
        if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(1.0, 0.5)) {
            QueueFluState(flu, ComponentFluPacket.EventType.SyncStat);
        }
    }

    public override void OnNetworkFluSneeze(ComponentFlu flu) {
        QueueFluState(flu, ComponentFluPacket.EventType.Sneeze);
    }

    public override void OnNetworkFluStarted(ComponentFlu flu) {
        QueueFluState(flu, ComponentFluPacket.EventType.StartFlu);
    }

    public override void OnNetworkFluImpulse(ComponentFlu flu, Vector3 impulse) {
        if (!NetworkManager.IsClientRunning && flu.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                flu.Entity.Project,
                NetworkStateRevisionDomain.Flu,
                flu.Entity.Id);
            NetworkManager.Queue(new ComponentFluPacket(flu, impulse, revision));
        }
    }

    public override void OnNetworkFurnaceUpdate(ComponentFurnace furnace) {
        if (NetworkManager.IsServerRunning
            && furnace?.Id > 0
            && Time.PeriodicEvent(2.0, 0.0)) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                furnace.Project,
                NetworkStateRevisionDomain.Furnace,
                furnace.Id);
            NetworkManager.Queue(new ComponentFurnacePacket(furnace, revision));
        }
    }

    public override void OnNetworkManualLightingStrike(ComponentGui gui, Vector3 position, Vector3 direction) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SubsystemSkyPacket(position, direction));
    }

    public override void OnNetworkManualTimeChange(ComponentGui gui, double totalElapsedGameTime, double timeOfDayOffset) {
        if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(SubsystemTimePacket.CreateRequest(timeOfDayOffset));
        }
        else if (GameManager.Project != null) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                NetworkStateRevisionDomain.Time,
                SubsystemTimePacket.EnvironmentStateId);
            NetworkManager.Queue(new SubsystemTimePacket(
                totalElapsedGameTime,
                timeOfDayOffset,
                revision));
        }
    }

    public override void OnNetworkManualWeatherChange(ComponentGui gui, int type) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SubsystemWeatherPacket(type));
        else
            QueueAuthoritativeWeatherState();
    }

    public override void OnNetworkPlayerCreativeFly(ComponentGui gui, ComponentPlayer player) {
        if (NetworkManager.IsServerRunning && player.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                player.Entity.Project,
                NetworkStateRevisionDomain.CreativeFly,
                player.Entity.Id);
            NetworkManager.Queue(PlayerCreativeFlyPacket.CreateState(
                player,
                player.ComponentLocomotion.IsCreativeFlyEnabled,
                revision: revision));
        }
        else if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new PlayerCreativeFlyPacket(player));
        }
    }

    public override void OnNetworkCreatureDied(ComponentHealth health) {
        if (!NetworkManager.IsClientRunning && health.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                health.Entity.Project,
                NetworkStateRevisionDomain.Health,
                health.Entity.Id);
            NetworkManager.Queue(new HealthSyncPacket(health, revision));
        }
    }

    public override void OnNetworkHealthFallDamage(ComponentHealth health, float fallDamage, string cause) {
        InjureRequestPacket.SendFallDamage(health, fallDamage, cause);
    }

    public override void OnNetworkHealthHealUpdate(ComponentHealth health) {
        if (!NetworkManager.IsClientRunning
            && health.Entity.Id > 0
            && Time.PeriodicEvent(2.0, 0.0)) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                health.Entity.Project,
                NetworkStateRevisionDomain.Health,
                health.Entity.Id);
            NetworkManager.Queue(new HealthSyncPacket(health, revision));
        }
    }

    public override void OnNetworkHealthInjured(ComponentHealth health, Injury injury, float amount) {
        if (!NetworkManager.IsClientRunning && health.Entity.Id > 0 && amount > 0f) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                health.Entity.Project,
                NetworkStateRevisionDomain.Health,
                health.Entity.Id);
            NetworkManager.Queue(new InjureEventPacket(
                health,
                injury.Attacker,
                amount,
                injury.Cause,
                injury.IgnoreInvulnerability,
                revision));
        }
    }

    public override void OnNetworkHumanModelRow(ComponentHumanModel model) {
        if (!NetworkManager.IsClientRunning && model.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                model.Entity.Project,
                NetworkStateRevisionDomain.HumanRow,
                model.Entity.Id);
            NetworkManager.Queue(new HumanRowBehaviorPacket(model, revision));
        }
    }

    public override void OnNetworkInventorySlotChanged(IInventory inventory, int slotIndex) {
        if (!NetworkManager.IsClientRunning
            && inventory?.Id > 0
            && inventory is not ComponentClothing) {
            NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                inventory.Project,
                NetworkStateRevisionDomain.Inventory,
                inventory.Id);
            NetworkManager.Queue(new InventorySyncPacket(inventory));
        }
    }

    public override void OnNetworkClothingSlotChanged(IInventory inventory, int slotIndex) {
        if (!NetworkManager.IsClientRunning
            && inventory is ComponentClothing clothing
            && clothing.Id > 0
            && clothing.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                clothing.Entity.Project,
                NetworkStateRevisionDomain.Clothing,
                clothing.Entity.Id);
            NetworkManager.Queue(new ClothingSyncPacket(clothing, revision));
        }
    }

    public override void OnNetworkLevelExperience(ComponentLevel level, int count, bool playSound) {
        if (!NetworkManager.IsClientRunning && level.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                level.Entity.Project,
                NetworkStateRevisionDomain.PlayerProgression,
                level.Entity.Id);
            NetworkManager.Queue(new PlayerAddExperiencePacket(
                level.m_componentPlayer.PlayerData,
                count,
                playSound,
                level.m_componentPlayer.PlayerData.Level,
                revision));
        }
    }

    public override void OnNetworkSetOnFire(ComponentOnFire onFire, ComponentCreature attacker, float duration) {
        if (!NetworkManager.IsClientRunning && onFire.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                onFire.Entity.Project,
                NetworkStateRevisionDomain.Fire,
                onFire.Entity.Id);
            NetworkManager.Queue(new EntityOnFirePacket(onFire, attacker, revision));
        }
    }

    public override void OnNetworkExtinguish(ComponentOnFire onFire) {
        if (!NetworkManager.IsClientRunning && onFire.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                onFire.Entity.Project,
                NetworkStateRevisionDomain.Fire,
                onFire.Entity.Id);
            NetworkManager.Queue(new EntityOnFirePacket(onFire, revision: revision));
        }
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
        if (!NetworkManager.IsClientRunning) {
            long revision = PickableLifecycleTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                pickable.Id);
            NetworkManager.Queue(new PickableSetFlyToPositionPacket(pickable, revision));
        }
    }

    public override void OnNetworkActiveSlotChanged(ComponentPlayer player) {
        IInventory inventory = player.ComponentMiner?.Inventory;
        if (inventory == null || inventory.Id <= 0) {
            return;
        }
        if (NetworkManager.IsClientRunning) {
            if (player.PlayerData.IsMainPlayer) {
                NetworkManager.Queue(new ActiveSlotChangePacket(inventory));
            }
        }
        else {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                inventory.Project,
                NetworkStateRevisionDomain.Inventory,
                inventory.Id);
            NetworkManager.Queue(new ActiveSlotChangePacket(inventory, revision));
        }
    }

    public override void OnMinerDig(ComponentMiner miner, TerrainRaycastResult raycastResult, ref float digProgress, out bool digged) {
        digged = false;
        NetworkDigConfirmationRuntime.ObserveMinerDig(miner, raycastResult);
    }

    public override void OnBlockDug(
        ComponentMiner componentMiner,
        BlockPlacementData digValue,
        int cellValue,
        ref int durabilityReduction,
        ref bool mute,
        ref int playerDataDugAdd
    ) {
        NetworkDigConfirmationRuntime.ObserveBlockDug(componentMiner, digValue, cellValue);
    }

    public override void OnNetworkPlayerAim(ComponentPlayer player) {
        if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
        }
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
        if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
        }
        NetworkManager.Queue(new PlayerDropPacket(player));
    }

    public override void OnNetworkRandomFeedBehaviorChanged(ComponentRandomFeedBehavior behavior, bool isFeed) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureRandomFeed,
                behavior.Entity.Id);
            NetworkManager.Queue(new RandomFeedBehaviorPacket(behavior, isFeed, revision));
        }
    }

    public override void OnNetworkRandomPeckBehaviorChanged(ComponentRandomPeckBehavior behavior, bool isPeck) {
        if (!NetworkManager.IsClientRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureRandomPeck,
                behavior.Entity.Id);
            NetworkManager.Queue(new RandomPeckBehaviorPacket(behavior, isPeck, revision));
        }
    }

    public override void OnNetworkRiderDismounting(ComponentRider rider, bool isRequest) {
        if (NetworkManager.IsClientRunning && isRequest) {
            NetworkManager.Queue(new ComponentMountPacket(rider, isRequest: true));
        }
        else if (!NetworkManager.IsClientRunning && !isRequest && rider.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                rider.Entity.Project,
                NetworkStateRevisionDomain.Mount,
                rider.Entity.Id);
            NetworkManager.Queue(ComponentMountPacket.CreateState(rider, null, revision));
        }
    }

    public override void OnNetworkRiderMounting(ComponentRider rider, ComponentMount mount, bool isRequest) {
        if (NetworkManager.IsClientRunning && isRequest) {
            NetworkManager.Queue(new ComponentMountPacket(rider, mount, isRequest: true));
        }
        else if (!NetworkManager.IsClientRunning && !isRequest && rider.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                rider.Entity.Project,
                NetworkStateRevisionDomain.Mount,
                rider.Entity.Id);
            NetworkManager.Queue(ComponentMountPacket.CreateState(rider, mount, revision));
        }
    }

    public override void OnNetworkSicknessChanged(ComponentSickness sickness) {
        QueueSicknessState(sickness, ComponentSicknessPacket.EventType.SyncStat);
    }

    public override void OnNetworkNauseaEffect(ComponentSickness sickness) {
        QueueSicknessState(sickness, ComponentSicknessPacket.EventType.NauseaEffect);
    }

    private static void QueueFluState(
        ComponentFlu flu,
        ComponentFluPacket.EventType eventType) {
        if (!NetworkManager.IsServerRunning || flu?.Entity.Id <= 0) return;
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            flu.Entity.Project,
            NetworkStateRevisionDomain.Flu,
            flu.Entity.Id);
        NetworkManager.Queue(new ComponentFluPacket(flu, eventType, revision));
    }

    private static void QueueSicknessState(
        ComponentSickness sickness,
        ComponentSicknessPacket.EventType eventType) {
        if (!NetworkManager.IsServerRunning || sickness?.Entity.Id <= 0) return;
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            sickness.Entity.Project,
            NetworkStateRevisionDomain.Sickness,
            sickness.Entity.Id);
        NetworkManager.Queue(new ComponentSicknessPacket(sickness, eventType, revision));
    }

    private static void QueuePeriodicPlayerStats() {
        if (!NetworkManager.IsServerRunning
            || !Time.PeriodicEvent(2.0, 0.75)
            || GameManager.Project is not Project project) {
            return;
        }
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>();
        if (players == null) return;
        foreach (ComponentPlayer player in players.ComponentPlayers.Where(player =>
                     player?.Entity.Id > 0)) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                project,
                NetworkStateRevisionDomain.PlayerStats,
                player.Entity.Id);
            NetworkManager.Queue(new PlayerStatSyncPacket(player, revision));
        }
    }

    public override void OnNetworkStubbornSteedHeadShakeOrder(ComponentStubbornSteedBehavior behavior, float headShakeOrder) {
        if (NetworkManager.IsServerRunning && behavior.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                behavior.Entity.Project,
                NetworkStateRevisionDomain.CreatureStubbornSteed,
                behavior.Entity.Id);
            NetworkManager.Queue(new StubbornSteedBehaviorPacket(
                behavior,
                headShakeOrder,
                revision));
        }
    }

    public override void OnNetworkVitalStatsUpdate(ComponentVitalStats stats) {
        if (NetworkManager.IsServerRunning && Time.PeriodicEvent(0.2, 0.0)) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                stats.Entity.Project,
                NetworkStateRevisionDomain.VitalStats,
                stats.Entity.Id);
            NetworkManager.Queue(new VitalStatSyncPacket(stats, revision));
        }
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

    public override void OnNetworkExplosion(
        Dictionary<Point2, List<(Point3, float)>> explosionCells,
        Vector3 position,
        float pressure,
        bool playExplosionSound) {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null) {
            return;
        }
        SubsystemExplosions subsystem = GameManager.Project.FindSubsystem<SubsystemExplosions>();
        long eventId = subsystem?.NetworkEventId ?? 0;
        if (eventId <= 0) {
            return;
        }
        IReadOnlyList<ExplosionPacket> packets = ExplosionPacket.CreateChunks(
            explosionCells,
            eventId,
            position,
            pressure,
            playExplosionSound);
        if (packets.Count == 0) {
            return;
        }
        ExplosionEventTracker.RecordGeneratedCells(
            GameManager.Project,
            eventId,
            packets.Count,
            packets.Sum(packet => packet.m_cells.Count));
        if (playExplosionSound) {
            ExplosionEventTracker.RecordGeneratedSound(
                GameManager.Project,
                eventId);
        }
        foreach (ExplosionPacket packet in packets) {
            NetworkManager.Queue(packet);
        }
    }

    public override void OnNetworkFireBlockAdded(int x, int y, int z, float expandability) {
        if (NetworkManager.IsServerRunning && GameManager.Project != null) {
            NetworkManager.Queue(new FireBlockAddPacket(x, y, z, expandability));
        }
    }

    public override void OnNetworkFireBlockRemoved(int x, int y, int z) {
        if (NetworkManager.IsServerRunning && GameManager.Project != null) {
            NetworkManager.Queue(new FireBlockRemovePacket(x, y, z));
        }
    }

    public override void OnNetworkFurnacesUpdate(SubsystemFurnaceBlockBehavior subsystem, List<ComponentFurnace> furnaces) {
        if (NetworkManager.IsServerRunning) {
            ComponentFurnace[] trackedFurnaces = furnaces
                .Where(furnace => furnace?.Id > 0 && furnace.Entity?.Id > 0)
                .ToArray();
            foreach (ComponentFurnace furnace in trackedFurnaces) {
                NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    furnace.Project,
                    NetworkStateRevisionDomain.Furnace,
                    furnace.Id);
            }
            foreach (ComponentFurnace[] batch in trackedFurnaces.Chunk(
                         ComponentFurnacePacket.MaximumFurnaceCount)) {
                NetworkManager.Queue(new ComponentFurnacePacket(batch));
            }
        }
    }

    public override void OnNetworkTimeUpdate(double totalElapsedGameTime, double timeOfDayOffset) {
        if (!NetworkManager.IsClientRunning && GameManager.Project != null) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                NetworkStateRevisionDomain.Time,
                SubsystemTimePacket.EnvironmentStateId);
            NetworkManager.Queue(new SubsystemTimePacket(
                totalElapsedGameTime,
                timeOfDayOffset,
                revision));
        }
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
        if (NetworkManager.IsServerRunning
            && !NetworkManager.IsClientRunning
            && GameManager.Project != null)
            NetworkManager.Queue(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
    }

    public override void OnNetworkMovingBlockRemoved(IMovingBlockSet movingBlockSet, bool isCrash) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new MovingBlockRemovePacket(movingBlockSet, isCrash));
    }

    public override void OnNetworkFurnitureHammerRequest(FurnitureDesign design, CellFace start) {
        if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new FurnitureHammerPacket(design, start));
        }
    }
    public override void OnNetworkPickableCreated(Pickable pickable) {
        long revision = PickableLifecycleTracker.AdvanceAuthoritativeRevision(
            GameManager.Project,
            pickable.Id);
        NetworkManager.Queue(new PickableCreatePacket(pickable, revision));
    }

    public override void OnNetworkPickableDeleted(Pickable pickable) {
        long revision = PickableLifecycleTracker.AdvanceAuthoritativeRevision(
            GameManager.Project,
            pickable.Id);
        NetworkManager.Queue(new PickableDeletePacket(pickable, revision));
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
        if (!NetworkManager.IsClientRunning && GameManager.Project != null) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                NetworkStateRevisionDomain.Sky,
                SubsystemTimePacket.EnvironmentStateId);
            NetworkManager.Queue(new SubsystemSkyPacket(targetPosition, revision));
        }
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
        if (!NetworkManager.IsClientRunning) {
            long revision = TerrainRevisionTracker.AdvanceAuthoritativeRevision(
                subsystem,
                x >> 4,
                z >> 4);
            NetworkManager.Queue(new TerrainChangeCellPacket(x, y, z, value, revision));
        }
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
        QueueAuthoritativeWeatherState();
    }

    public override void OnNetworkPrecipitationChanged(double startTime, double endTime, float intensity, float rampTime) {
        QueueAuthoritativeWeatherState();
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
        if (NetworkManager.IsClientRunning && playerData.ComponentPlayer != null) {
            NetworkManager.Queue(new PlayerBodyUpdatePacket(playerData.ComponentPlayer));
        }
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
        if (NetworkManager.IsClientRunning && !playerData.IsMainPlayer) {
            return;
        }
        NetworkManager.SetState(NetworkState.Playing);
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new StatePacket(NetworkManager.PlayerIndex, NetworkState.Playing));
    }

    public override void OnNetworkSleep(ComponentSleep sleep, bool allowManualWakeup) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new SleepRequestPacket(sleep, allowManualWakeup));
        if (!NetworkManager.IsClientRunning && sleep.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                sleep.Entity.Project,
                NetworkStateRevisionDomain.Sleep,
                sleep.Entity.Id);
            NetworkManager.Queue(new SleepStatusPacket(
                sleep,
                allowManualWakeup,
                true,
                revision: revision));
        }
    }

    public override void OnNetworkWakeUp(ComponentSleep sleep) {
        if (NetworkManager.IsClientRunning)
            NetworkManager.Queue(new WakeupRequestPacket(sleep));
        if (!NetworkManager.IsClientRunning && sleep.Entity.Id > 0) {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                sleep.Entity.Project,
                NetworkStateRevisionDomain.Sleep,
                sleep.Entity.Id);
            NetworkManager.Queue(new WakeupStatusPacket(sleep, revision));
        }
    }

    public override void OnNetworkPlayerBodyUpdate(ComponentPlayer player, bool isMainPlayer) {
        if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(0.2f, 0.0)) {
            foreach (ClientSession session in NetworkManager.ServerSessions) {
                if (session.NetworkState < NetworkState.Playing || session.PlayerIndex == player.PlayerData.PlayerIndex)
                    continue;
                NetworkManager.Queue(new PlayerBodyUpdatePacket(
                    player,
                    session,
                    forceFull: true) { To = session });
            }
        }
        if (isMainPlayer && Time.PeriodicEvent(0.2f, 0.0))
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player, forceFull: true));
    }

    public override void OnNetworkTerrainPlayerLocationUpdate(TerrainUpdater updater, TerrainUpdater.UpdateLocation location) {
        if (NetworkManager.IsClientRunning && NetworkManager.PlayerIndex >= 0)
            NetworkManager.Queue(new TerrainViewDistancePacket(location));
    }

    public override void OnNetworkBodyUpdate(ComponentBody body, float dt) {
        if (NetworkManager.IsServerRunning) {
            NetworkBodyInterpolation.UpdateServer(body, dt);
        }
        else {
            NetworkBodyInterpolation.Update(body, dt);
        }

        body.UpdateImmersionData();

        Vector3 position = body.Position;
        int value = body.m_subsystemTerrain.Terrain.GetCellValue(
            Terrain.ToCell(position.X),
            Terrain.ToCell(position.Y - 0.01f),
            Terrain.ToCell(position.Z)
        );
        body.StandingOnValue = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetIsCollidable(body, value)
            ? value
            : null;
    }

    public override void OnNetworkBodyRemoved(ComponentBody body) {
        NetworkBodyInterpolation.RemoveTarget(body);
        HandleMountBodyRemoved(body);
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

    private static void QueueAuthoritativeWeatherState() {
        if (NetworkManager.IsClientRunning || GameManager.Project == null) {
            return;
        }
        SubsystemWeather weather = GameManager.Project.FindSubsystem<SubsystemWeather>();
        if (weather == null) {
            return;
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            GameManager.Project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        NetworkManager.Queue(new SubsystemWeatherPacket(weather, revision));
    }

    private static void HandleMountBodyRemoved(ComponentBody body) {
        if (NetworkManager.IsClientRunning
            || body?.Entity?.Id <= 0
            || body.Entity.FindComponent<ComponentMount>() == null) {
            return;
        }
        foreach (ComponentBody childBody in body.ChildBodies.ToArray()) {
            ComponentRider rider = childBody.Entity.FindComponent<ComponentRider>();
            if (rider == null || rider.Entity.Id <= 0) {
                continue;
            }
            rider.ApplyAuthoritativeMountState(null, animate: false);
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                rider.Entity.Project,
                NetworkStateRevisionDomain.Mount,
                rider.Entity.Id);
            NetworkManager.Queue(ComponentMountPacket.CreateState(rider, null, revision));
        }
    }

    }
}
