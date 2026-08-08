namespace Survivalcraft.Testing.Control;

/// <summary>
/// Stable, reviewable identifiers for the production entry points exercised by
/// typed automation actions. They are evidence labels, not executable dispatch.
/// </summary>
public static class ProductionPathEntries {
    public const string CreativeFly = "ComponentGui.TrySetCreativeFlyEnabled";
    public const string PlayerMotion = "ComponentInput -> ComponentPlayer.Update";
    public const string PlayerTerrainInteraction = "ComponentInput -> ComponentMiner.Update";
    public const string EntityImpulse = "ComponentBody.ApplyImpulse";
    public const string EntityLifecycle = "Project.AddEntity/RemoveEntity -> NetworkModLoader";
    public const string PickableLifecycle =
        "SubsystemPickables.AddPickable/Update -> NetworkModLoader";
    public const string PickableAttraction =
        "OnNetworkPickableAttract -> PickableSetFlyToPositionPacket";
    public const string PlayerProjectileFire =
        "ComponentInput -> ComponentPlayer.Update -> PlayerAimPacket -> SubsystemCrossbowBlockBehavior.OnAim";
    public const string InventoryMove = "MoveItemPacket.Handle";
    public const string InventorySelectSlot =
        "ComponentInventoryBase.ActiveSlotIndex -> ActiveSlotChangePacket.Handle";
    public const string InventoryDrop =
        "ComponentInput -> ComponentPlayer.OnNetworkPlayerDrop";
    public const string InventoryOpenContainer =
        "OpenInventoryByPointPacket/OpenInventoryByIDPacket.Handle -> OpenInventoryResultPacket.Handle";
    public const string FurnitureHammer =
        "FurnitureHammerPacket.Handle -> SubsystemFurnitureBlockBehavior.TryPrepareNetworkFurnitureHammer/CommitFurnitureHammer";
    public const string InventoryRevision = "InventorySyncPacket.Handle";
    public const string FurnaceRevision = "ComponentFurnacePacket.Handle";
    public const string PlayerHit = "ComponentInput -> ComponentMiner.Hit";
    public const string PlayerHitFault = "PlayerHitPacket.Handle";
    public const string PlayerRespawn =
        "PlayerData.readyToRestart -> ComponentPlayer.Update";
    public const string PlayerRespawnFault = "PlayerRestartPacket.Handle";
    public const string HealthRevision = "HealthSyncPacket.Handle";
    public const string EnvironmentTime = "SubsystemTimePacket.Handle";
    public const string EnvironmentWeather = "SubsystemWeatherPacket.Handle";
    public const string EnvironmentSky = "SubsystemSkyPacket.Handle";
    public const string SleepRequest = "SleepRequestPacket.Handle";
    public const string WakeRequest = "WakeupRequestPacket.Handle";
    public const string FireState = "EntityOnFirePacket.Handle";
    public const string WorldFireLifecycle =
        "SubsystemFireBlockBehavior.AddFire/RemoveFire -> FireBlockAddPacket/FireBlockRemovePacket.Handle";
    public const string ExplosionEvent =
        "SubsystemExplosions.AddExplosion -> ExplosionPacket.Handle";
    public const string MountState = "ComponentMountPacket.Handle";
    public const string LogicEdit = "Editable logic Packet.Handle";
    public const string LogicInteract = "PlayerInteractPacket.Handle";
    public const string LogicRevision =
        "Editable logic Packet.Handle + SubsystemElectricityPacket.Handle";
    public const string MovingBlockLifecycle =
        "SubsystemMovingBlocks.AddMovingBlockSet/RemoveMovingBlockSet";
    public const string PistonLifecycle =
        "SubsystemPistonBlockBehavior.AdjustPiston/StopPiston";
    public const string TerrainRevision = "TerrainSyncChunkListPacket.Handle";
}
