var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var body = entity.FindComponent<Game.ComponentBody>();
if (body == null) return "no-body";
var vehicle = entity.FindComponent<WonderfulEra.ComponentVehicle>();
var inventory = entity.FindComponent<WonderfulEra.ComponentVehicleInventory>();
var blockEntity = entity.FindComponent<WonderfulEra.ComponentNewBlockEntity>();
int blockValue = blockEntity?.BlockValue ?? 0;
bool running = WonderfulEra.KeyBlock.GetRunning(Game.Terrain.ExtractData(blockValue));
bool standing = body.StandingOnValue.HasValue || body.StandingOnBody != null;
int fuelCount = inventory != null ? inventory.GetSlotCount(inventory.FuelSlotIndex) : -1;
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "pos={0:R},{1:R},{2:R};vel={3:R},{4:R},{5:R};move={6:R};turn={7:R};run={8};energy={9:R};fuel={10};standing={11};slots={12}",
    body.Position.X,
    body.Position.Y,
    body.Position.Z,
    body.Velocity.X,
    body.Velocity.Y,
    body.Velocity.Z,
    vehicle?.MoveOrder ?? 0f,
    vehicle?.TurnOrder ?? 0f,
    running ? 1 : 0,
    inventory?.EnergyProcess ?? -1f,
    fuelCount,
    standing ? 1 : 0,
    inventory?.SlotsCount ?? -1);