var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var position = players.GlobalSpawnPosition + new Engine.Vector3(24f, 1f, 12f);
int x = (int)System.MathF.Floor(position.X);
int y = (int)System.MathF.Floor(position.Y);
int z = (int)System.MathF.Floor(position.Z);

int counterData = 4 << 2;
int counterValue = Game.Terrain.MakeBlockValue(Game.CounterBlock.Index, 0, counterData);

terrain.ChangeCell(x, y - 1, z, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
terrain.ChangeCell(x + 1, y - 1, z, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
terrain.ChangeCell(x + 2, y - 1, z, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
terrain.ChangeCell(x + 1, y, z, 0);
terrain.ChangeCell(x + 2, y, z, 0);
terrain.ChangeCell(x, y, z, counterValue);

var electricity = project.FindSubsystem<Game.SubsystemElectricity>();
float? persistent = electricity.ReadPersistentVoltage(new Engine.Point3(x, y, z));
var counter = electricity.GetElectricElement(x, y, z, 4) as Game.CounterElectricElement;
int topConnectorFace = Game.SubsystemElectricity.GetConnectorFace(4, Game.ElectricConnectorDirection.Top);
int value = terrain.Terrain.GetCellValue(x, y, z);
int sourceValue = terrain.Terrain.GetCellValue(x + 1, y, z);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};contents={3};data={4};sourceContents={5};sourceData={6};hasPersistent={7};persistent={8};output={9};counter={10};overflow={11};plusAllowed={12};circuitStep={13}",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(value),
    Game.Terrain.ExtractData(value),
    Game.Terrain.ExtractContents(sourceValue),
    Game.Terrain.ExtractData(sourceValue),
    persistent.HasValue,
    persistent.GetValueOrDefault(),
    counter?.GetOutputVoltage(topConnectorFace) ?? 0f,
    counter?.m_counter ?? 0,
    counter?.m_overflow ?? false,
    counter?.m_plusAllowed ?? false,
    electricity.CircuitStep);
