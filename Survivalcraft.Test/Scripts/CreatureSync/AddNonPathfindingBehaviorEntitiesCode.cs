var project = Game.GameManager.Project;
var player = project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.First();
var playerBody = player.ComponentBody;
var spawn = project.FindSubsystem<SubsystemCreatureSpawn>();
var terrain = project.FindSubsystem<SubsystemTerrain>().Terrain;

Engine.Vector3 FindSpawnPosition(float xOffset, float zOffset) {
    var position = playerBody.Position + new Engine.Vector3(xOffset, 0f, zOffset);
    int x = Terrain.ToCell(position.X);
    int z = Terrain.ToCell(position.Z);
    position.Y = System.Math.Max(terrain.GetTopHeight(x, z), 150f) + 1.1f;
    return position;
}

var entities = new[] {
    (Name: "FishOutOfWater", Entity: spawn.SpawnCreature("Bass_Freshwater", FindSpawnPosition(4f, 4f), false)),
    (Name: "LookAround", Entity: spawn.SpawnCreature("Duck", FindSpawnPosition(6f, 4f), false)),
    (Name: "StubbornSteed", Entity: spawn.SpawnCreature("Donkey_Saddled", FindSpawnPosition(8f, 4f), false))
};

return string.Join(";", entities.Select(e => e.Entity == null
    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:0", e.Name)
    : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}", e.Name, e.Entity.Id)));
