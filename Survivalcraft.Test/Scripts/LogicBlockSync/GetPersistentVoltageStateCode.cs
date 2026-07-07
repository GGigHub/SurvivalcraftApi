var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var electricity = project.FindSubsystem<Game.SubsystemElectricity>();
int x = __X__;
int y = __Y__;
int z = __Z__;

int value = terrain.Terrain.GetCellValue(x, y, z);
int sourceValue = terrain.Terrain.GetCellValue(x + 1, y, z);
float? persistent = electricity.ReadPersistentVoltage(new Engine.Point3(x, y, z));
var counter = electricity.GetElectricElement(x, y, z, 4) as Game.CounterElectricElement;
int topConnectorFace = Game.SubsystemElectricity.GetConnectorFace(4, Game.ElectricConnectorDirection.Top);

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
