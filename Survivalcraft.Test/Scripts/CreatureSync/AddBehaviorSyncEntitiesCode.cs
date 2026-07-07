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

int IdIfHas<T>(GameEntitySystem.Entity entity) where T : GameEntitySystem.Component =>
    entity?.FindComponent<T>() != null ? entity.Id : 0;

void PrepareTestEntity(GameEntitySystem.Entity entity) {
    if (entity == null) {
        return;
    }

    var health = entity.FindComponent<ComponentHealth>();
    if (health != null) {
        health.Health = 100000f;
        health.IsInvulnerable = true;
    }

    var despawn = entity.FindComponent<ComponentSpawn>();
    if (despawn != null) {
        despawn.AutoDespawn = false;
    }

    var body = entity.FindComponent<ComponentBody>();
    if (body != null) {
        body.Velocity = Engine.Vector3.Zero;
        if (!body.StandingOnValue.HasValue) {
            body.StandingOnValue = Terrain.MakeBlockValue(2);
        }
    }
}

var chaseCow = spawn.SpawnCreature("Cow", FindSpawnPosition(10f, 4f), false);
var eatCow = spawn.SpawnCreature("Cow", FindSpawnPosition(12f, 4f), false);
var feedCow = spawn.SpawnCreature("Cow", FindSpawnPosition(14f, 4f), false);
var ray = spawn.SpawnCreature("Ray_Yellow", FindSpawnPosition(16f, 4f), false);
var fish = spawn.SpawnCreature("Bass_Freshwater", FindSpawnPosition(18f, 4f), false);
var lookDuck = spawn.SpawnCreature("Duck", FindSpawnPosition(20f, 4f), false);
var peckDuck = spawn.SpawnCreature("Duck", FindSpawnPosition(22f, 4f), false);
var steed = spawn.SpawnCreature("Donkey_Saddled", FindSpawnPosition(24f, 4f), false);
var soundDuck = spawn.SpawnCreature("Duck", FindSpawnPosition(26f, 4f), false);

foreach (var entity in new[] { chaseCow, eatCow, feedCow, ray, fish, lookDuck, peckDuck, steed, soundDuck }) {
    PrepareTestEntity(entity);
}

return string.Join(";", new[] {
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "Chase:{0}", IdIfHas<ComponentChaseBehavior>(chaseCow)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "DigInMud:{0}", IdIfHas<ComponentDigInMudBehavior>(ray)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "EatPickable:{0}", IdIfHas<ComponentEatPickableBehavior>(eatCow)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "FishOutOfWater:{0}", IdIfHas<ComponentFishOutOfWaterBehavior>(fish)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "LookAround:{0}", IdIfHas<ComponentLookAroundBehavior>(lookDuck)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "RandomFeed:{0}", IdIfHas<ComponentRandomFeedBehavior>(feedCow)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "RandomPeck:{0}", IdIfHas<ComponentRandomPeckBehavior>(peckDuck)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "StubbornSteed:{0}", IdIfHas<ComponentStubbornSteedBehavior>(steed)),
    string.Format(System.Globalization.CultureInfo.InvariantCulture, "Sound:{0}", IdIfHas<ComponentCreatureSounds>(soundDuck))
});

