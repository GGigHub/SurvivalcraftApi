var project = Game.GameManager.Project;
if (project == null) return "no-project";
GameEntitySystem.Entity entity;
try {
    entity = Game.DatabaseManager.CreateEntity(project, "AE86_Car", true);
} catch (System.Exception ex) {
    return "create-failed:" + ex.GetType().Name;
}

var body = entity.FindComponent<Game.ComponentBody>();
var vehicle = entity.FindComponent<WonderfulEra.ComponentVehicle>();
var inventory = entity.FindComponent<WonderfulEra.ComponentVehicleInventory>();
var carInventory = entity.FindComponent<WonderfulEra.ComponentCarInventory>();
var blockEntity = entity.FindComponent<WonderfulEra.ComponentNewBlockEntity>();
var mount = entity.FindComponent<Game.ComponentMount>();
var creature = entity.FindComponent<Game.ComponentCreature>();
var damage = entity.FindComponent<Game.ComponentDamage>();
var onFire = entity.FindComponent<Game.ComponentOnFire>();

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "vehicle={0};vehicleInventory={1};carInventory={2};blockEntity={3};mount={4};creature={5};damage={6};onFire={7};slots={8};blockValue={9}",
    vehicle != null ? 1 : 0,
    inventory != null ? 1 : 0,
    carInventory != null ? 1 : 0,
    blockEntity != null ? 1 : 0,
    mount != null ? 1 : 0,
    creature != null ? 1 : 0,
    damage != null ? 1 : 0,
    onFire != null ? 1 : 0,
    inventory?.SlotsCount ?? -1,
    blockEntity?.BlockValue ?? 0);