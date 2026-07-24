try {
    var project = Game.GameManager.Project;
    var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
    var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
    int x = 143, y = 66, z = 96;

    terrain.ChangeCell(x, y, z, Game.Terrain.MakeBlockValue(Game.FurnaceBlock.Index));
    var localEntity = blockEntities.GetBlockEntity(x, y, z)?.Entity;
    if (localEntity == null) return "error: local block entity not created";

    var savedList = project.SaveEntities(new[] { localEntity });
    int localId = localEntity.Id;

    savedList.EntitiesData[0].Id = localId + 1000;
    var vd = new TemplatesDatabase.ValuesDictionary();
    savedList.Save(vd);
    var networkDataList = new GameEntitySystem.EntityDataList(project.GameDatabase, vd, false);

    var packet = new Game.Network.EntityLoadPacket { entityDataList = networkDataList };
    packet.Handle(isServer: false);

    return "no_exception";
}
catch (System.Exception ex) {
    return ex.ToString();
}
