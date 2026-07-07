var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var movingBlocks = project.FindSubsystem<Game.SubsystemMovingBlocks>();
var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
int x = __X__;
int y = __Y__;
int z = __Z__;
var point = new Engine.Point3(x, y, z);
int value = terrain.Terrain.GetCellValue(x, y, z);

var pistonSets = movingBlocks
    .MovingBlockSetEnumerable
    .Where(s => s.Id == Game.SubsystemPistonBlockBehavior.IdString && object.Equals(s.Tag, point))
    .Distinct()
    .ToList();
string pistonBlocks = string.Join(
    "|",
    pistonSets
        .SelectMany(s => s.Blocks)
        .OrderBy(b => b.Offset.X)
        .ThenBy(b => b.Offset.Y)
        .ThenBy(b => b.Offset.Z)
        .ThenBy(b => b.Value)
        .Select(b => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0},{1},{2}:{3}",
            b.Offset.X,
            b.Offset.Y,
            b.Offset.Z,
            b.Value)));
var dispenserEntity = blockEntities.GetBlockEntity(point);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};contents={3};data={4};pistonMoving={5};pistonBlocks={6};dispenserEntity={7}",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(value),
    Game.Terrain.ExtractData(value),
    pistonSets.Count,
    pistonBlocks,
    dispenserEntity != null);
