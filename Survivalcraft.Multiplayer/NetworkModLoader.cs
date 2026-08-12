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
        private const double FurnaceStateSyncPeriod = 0.1;

        public override void __ModInitialize() {

            string[] hookNames = [
                "IsNetworkMode",
                "OnMinerDig",
                "OnBlockDug",
                "GuiUpdate",
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
                "OnNetworkParticleSystemAdded",
                "OnNetworkPickableAttract",
                "OnNetworkActiveSlotChanged",
                "OnNetworkPlayerAim",
                "OnNetworkPlayerDig",
                "OnNetworkPlayerHit",
                "OnNetworkPlayerInteract",
                "OnNetworkPlayerPlace",
                "OnNetworkPlayerDrop",
                "OnPlayerDroppedItems",
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
                "OnProjectileRaycastBodyBox",
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
                "OnIUpdateableUpdate",
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
                NetworkDigConfirmationRuntime.Tick();
                NetworkPlaceConfirmationRuntime.Tick();
                InventoryPacketHelpers.FlushPendingSync();
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
                NetworkBodyHistory.Clear();
                PlayerBodyUpdatePacket.ClearSnapshots();
                EntityBodyUpdatePacket.Clear();
                FurnitureHammerPacket.ClearState();
                NetworkDigConfirmationRuntime.Reset();
                NetworkPlaceConfirmationRuntime.Reset();
                InventoryPacketHelpers.ClearPendingSync();
                ChatManager.Clear();
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
                    Log.Warning($"Failed to start relay host: {ex.Message}");
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

        public override void GuiUpdate(ComponentGui componentGui) {
            GameWidget gameWidget = componentGui.GameWidget;
            ContainerWidget guiWidget = gameWidget.GuiWidget;
            NetworkStatusWidget networkStatusWidget = FindOrMoveToGuiRoot<NetworkStatusWidget>(guiWidget);
            if (networkStatusWidget == null) {
                networkStatusWidget = new NetworkStatusWidget {
                    Name = "RelayCodeLabel",
                    Color = Color.White,
                    DropShadow = true,
                    HorizontalAlignment = WidgetAlignment.Far,
                    VerticalAlignment = WidgetAlignment.Near,
                    MarginLeft = 10f,
                    MarginTop = 10f,
                    MarginRight = 10f,
                    MarginBottom = 10f
                };
                guiWidget.AddChildren(networkStatusWidget);
            }
            networkStatusWidget.Color = Color.White;
            networkStatusWidget.DropShadow = true;
            networkStatusWidget.HorizontalAlignment = WidgetAlignment.Far;
            networkStatusWidget.VerticalAlignment = WidgetAlignment.Near;
            networkStatusWidget.MarginLeft = 10f;
            networkStatusWidget.MarginTop = 10f;
            networkStatusWidget.MarginRight = 10f;
            networkStatusWidget.MarginBottom = 10f;

            PlayerNameArrowWidget playerNameArrowWidget = FindOrMoveToGuiRoot<PlayerNameArrowWidget>(guiWidget);
            if (playerNameArrowWidget == null) {
                playerNameArrowWidget = new PlayerNameArrowWidget();
                guiWidget.AddChildren(playerNameArrowWidget);
            }
            playerNameArrowWidget.HorizontalAlignment = WidgetAlignment.Near;
            playerNameArrowWidget.VerticalAlignment = WidgetAlignment.Near;
            playerNameArrowWidget.MarginLeft = 10f;
            playerNameArrowWidget.MarginTop = 10f;
            playerNameArrowWidget.MarginRight = 10f;
            playerNameArrowWidget.MarginBottom = 10f;
            playerNameArrowWidget.SetGameWidget(gameWidget);

            // Keep the chat in the same control layer and position as the
            // original NetMessageWidget: top-right, 68 px from the edge.
            CanvasWidget controlsWidget = guiWidget.Children.Find<CanvasWidget>("ControlsContainer", false);
            ChatWidget chatWidget = controlsWidget?.Children.Find<ChatWidget>(false)
                ?? guiWidget.Children.Find<ChatWidget>(false);
            if (chatWidget == null) {
                chatWidget = new ChatWidget();
            }
            if (controlsWidget != null && chatWidget.ParentWidget != controlsWidget) {
                chatWidget.ParentWidget?.RemoveChildren(chatWidget);
                controlsWidget.AddChildren(chatWidget);
            }
            else if (controlsWidget == null && chatWidget.ParentWidget != guiWidget) {
                chatWidget.ParentWidget?.RemoveChildren(chatWidget);
                guiWidget.AddChildren(chatWidget);
            }

            chatWidget.HorizontalAlignment = WidgetAlignment.Far;
            chatWidget.VerticalAlignment = WidgetAlignment.Near;
            chatWidget.Margin = new Vector2(68f, 5f);
            chatWidget.SetGameWidget(gameWidget);
        }

        static T FindOrMoveToGuiRoot<T>(ContainerWidget guiWidget) where T : Widget {
            T widget = guiWidget.Children.Find<T>(false);
            if (widget != null && widget.ParentWidget != guiWidget) {
                widget.ParentWidget?.RemoveChildren(widget);
                guiWidget.AddChildren(widget);
            }
            return widget;
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

    public override void OnIUpdateableUpdate(SubsystemUpdate subsystemUpdate,
        IUpdateable updateable,
        float dt,
        out bool skip) {
        skip = false;
        if (!NetworkManager.IsClientRunning) {
            return;
        }

        if (updateable is (ComponentChaseBehavior
                or ComponentDigInMudBehavior
                or ComponentEatPickableBehavior
                or ComponentFishOutOfWaterBehavior
                or ComponentLookAroundBehavior
                or ComponentRandomFeedBehavior
                or ComponentRandomPeckBehavior)) {
            return;
        }

        if (updateable is not (ComponentBehavior
                or ComponentBehaviorSelector
                or ComponentPathfinding
                or ComponentPilot
                or ComponentLocomotion)) {
            return;
        }

        ComponentBody body = (updateable as Component)?.Entity?.FindComponent<ComponentBody>();
        skip = NetworkManager.IsServerAuthoritativeCreatureBody(body);
    }

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
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.ChaseAttackChanged,
                value: isAttack);
    }

    public override void OnNetworkDamage(ComponentDamage damage) {
        if (!NetworkManager.IsClientRunning)
            NetworkManager.Queue(new DamageSyncPacket(damage));
    }

    public override void OnNetworkDigInMudBehaviorChanged(ComponentDigInMudBehavior behavior, bool isDigIn) {
        if (!NetworkManager.IsClientRunning)
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.DigInMudChanged,
                value: isDigIn);
    }

    public override void OnNetworkEatPickableBehaviorChanged(ComponentEatPickableBehavior behavior, bool isFeed) {
        if (!NetworkManager.IsClientRunning)
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.EatPickableChanged,
                value: isFeed);
    }

    public override void OnNetworkFishOutOfWaterBehaviorChanged(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
        if (!NetworkManager.IsClientRunning)
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.FishOutOfWaterChanged,
                value: isBend);
    }

    public override void OnNetworkLookAroundBehaviorChanged(ComponentLookAroundBehavior behavior, bool isLookRandom) {
        if (!NetworkManager.IsClientRunning)
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.LookAroundChanged,
                value: isLookRandom);
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
        if (NetworkManager.IsServerRunning && Time.PeriodicEvent(FurnaceStateSyncPeriod, 0.0))
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
        if (NetworkManager.IsServerRunning) {
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                model.Entity.Id,
                NetworkBehaviorEventType.HumanRow,
                value: model.m_rowLeft,
                secondaryValue: model.m_rowRight);
        }
    }

    public override void OnNetworkInventorySlotChanged(IInventory inventory, int slotIndex) {
        if (!NetworkManager.IsClientRunning)
            InventoryPacketHelpers.MarkInventoryDirty(inventory);
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

    public override void OnNetworkPlayerPlace(
        ComponentPlayer player,
        ComponentPlayer.InteractEventItem interactEvent,
        bool placed
    ) {
        NetworkPlaceConfirmationRuntime.ObserveServerPlacement(player, interactEvent, placed);
    }

    public override void OnNetworkPlayerDrop(ComponentPlayer player) {
        if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
        }
        NetworkManager.Queue(new PlayerDropPacket(player));
    }

    public override void OnPlayerDroppedItems(ComponentPlayer player, IInventory inventory, int slotIndex, int count) {
        // ComponentCreativeInventory removes items without firing the normal
        // inventory-change hook, so sync it explicitly. Q-drop and drag-drop
        // share this unified drop path (ComponentPlayer.DropInventoryItems).
        if (NetworkManager.IsServerRunning && inventory is ComponentCreativeInventory) {
            InventoryPacketHelpers.QueueInventorySync(inventory);
        }
    }

    public override void OnNetworkRandomFeedBehaviorChanged(ComponentRandomFeedBehavior behavior, bool isFeed) {
        if (!NetworkManager.IsClientRunning)
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.RandomFeedChanged,
                value: isFeed);
    }

    public override void OnNetworkRandomPeckBehaviorChanged(ComponentRandomPeckBehavior behavior, bool isPeck) {
        if (!NetworkManager.IsClientRunning)
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.RandomPeckChanged,
                value: isPeck);
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
        if (NetworkManager.IsServerRunning) {
            EntityBodyUpdatePacket.QueueBehaviorEvent(
                behavior.Entity.Id,
                NetworkBehaviorEventType.StubbornSteedHeadShake,
                amount: headShakeOrder);
        }
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

    public override void OnNetworkExplosion(Dictionary<Point2, List<(Point3, float)>> explosionCells, Vector3 position, float pressure, bool playExplosionSound) {
        NetworkManager.Queue(new ExplosionPacket(explosionCells, position, pressure, playExplosionSound));
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

    public override void OnNetworkFurnitureHammerRequest(FurnitureDesign design, CellFace start) {
        if (NetworkManager.IsClientRunning) {
            NetworkManager.Queue(new FurnitureHammerPacket(design, start));
        }
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
        if (NetworkManager.IsServerRunning) {
            NetworkBodyInterpolation.UpdateServer(body, dt);
        }
        else {
            NetworkBodyInterpolation.Update(body, dt);
        }
        if (NetworkManager.IsServerRunning && NetworkBodyHistory.ShouldRecord(body)) {
            NetworkBodyHistory.Record(body);
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
        NetworkBodyHistory.RemoveTarget(body);
    }

    public override void OnNetworkBodyUpdated(ComponentBody body, float dt) {
        if (NetworkManager.IsServerRunning && NetworkBodyHistory.ShouldRecord(body)) {
            NetworkBodyHistory.Record(body);
        }
        EntityBodyUpdatePacket.CollectBodyForSync(body, dt);
    }

    public override void OnProjectileRaycastBodyBox(Projectile projectile, ref Func<ComponentBody, BoundingBox> boundingBoxProvider) {
        if (!NetworkManager.IsServerRunning || projectile?.OwnerEntity == null) {
            return;
        }
        // 只有远程玩家发射的弹射物需要回溯补偿：
        // 房主本人看到的生物是实时位置，远程玩家的视角则渲染在延迟时间线上。
        ComponentPlayer ownerPlayer = projectile.OwnerEntity.FindComponent<ComponentPlayer>();
        if (ownerPlayer == null || ownerPlayer.PlayerData.IsMainPlayer) {
            return;
        }
        // 客户端实际渲染延迟在 [RemoteBodyDelay, RemoteBodyDelay + DefaultSnapshotInterval] 附近摆动，
        // 因此在延迟窗口内采样并合并包围盒，覆盖客户端屏幕上目标可能出现的位置。
        // 发射者本人的身体在其客户端是实时渲染的，不参与补偿。
        ComponentBody ownerBody = ownerPlayer.ComponentBody;
        boundingBoxProvider = body =>
            NetworkBodyHistory.TryGetCompensatedBoxAt(body, Engine.Time.RealTime, ownerBody, out BoundingBox box)
                ? box
                : body.BoundingBox;
    }

    public override void OnNetworkEditableItemDataHarvested<T>(int id, T data) {
        if (data is MemoryBankData memoryBankData)
            OnNetworkMemoryBankDataHarvested(id, memoryBankData);
        else if (data is TruthTableData truthTableData)
            OnNetworkTruthTableDataHarvested(id, truthTableData);
    }

    }
}
