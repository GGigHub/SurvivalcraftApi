var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;
var point = new Engine.Point3(x, y, z);
int value = terrain.Terrain.GetCellValue(x, y, z);

string memory = project
    .FindSubsystem<Game.SubsystemMemoryBankBlockBehavior>()
    .GetBlockData(point)
    ?.SaveString(false) ?? string.Empty;
string truth = project
    .FindSubsystem<Game.SubsystemTruthTableCircuitBlockBehavior>()
    .GetBlockData(point)
    ?.SaveString() ?? string.Empty;
var signData = project
    .FindSubsystem<Game.SubsystemSignBlockBehavior>()
    .GetSignData(point);
string signLines = signData == null ? string.Empty : string.Join("|", signData.Lines);
string signColors = signData == null
    ? string.Empty
    : string.Join("|", signData.Colors.Select(c => c.PackedValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
string signUrl = signData?.Url ?? string.Empty;

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};contents={3};data={4};memory={5};truth={6};signLines={7};signColors={8};signUrl={9}",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(value),
    Game.Terrain.ExtractData(value),
    memory,
    truth,
    signLines,
    signColors,
    signUrl);
