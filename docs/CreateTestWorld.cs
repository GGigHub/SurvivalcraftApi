// 创建一个测试用创造存档，玩家模板已经初始化好了
public static WorldInfo CreateWorld() {
    WorldSettings worldSettings = new WorldSettings {
        Name = "Creative World",
        GameMode = GameMode.Creative,
        TerrainGenerationMode = TerrainGenerationMode.Continent,
        OriginalSerializationVersion = VersionsManager.SerializationVersion
    };
    WorldInfo worldInfo = CreateWorld(worldSettings);
    XElement projectNode = GetProjectNode(worldInfo);
    XElement playersNode = GetPlayersNode(projectNode);
    XElement playersDictNode = (from e in playersNode.Elements()
       where XmlUtils.GetAttributeValue<string>(e, "Name", string.Empty) == "Players"
       select e).First();
    ValuesDictionary playerData = new ValuesDictionary();
    playerData.SetValue("Name", "Player");
    playerData.SetValue("CharacterSkinName", "$Male1");
    playerData.SetValue("PlayerClass", PlayerClass.Male);
    playerData.SetValue("Level", 1f);
    playerData.SetValue("PlayerGUID", Network.NetworkManager.Self.PlayerGuid);
    playerData.SetValue("SpawnPosition", Vector3.Zero);
    playerData.SetValue("FirstSpawnTime", 0.0);
    playerData.SetValue("LastSpawnTime", 0.0);
    playerData.SetValue("SpawnsCount", 0);
    playerData.SetValue("InputDevice", WidgetInputDevice.None);
    XElement playerEntryNode = XmlUtils.AddElement(playersDictNode, "Values");
    XmlUtils.SetAttributeValue(playerEntryNode, "Name", "0");
    playerData.Save(playerEntryNode);
    string projectPath = Storage.CombinePaths(worldInfo.DirectoryName, "Project.xml");
    using (Stream stream = Storage.OpenFile(projectPath, OpenFileMode.Create)) {
        XmlUtils.SaveXmlToStream(projectNode, stream, null, true);
    }
    return GetWorldInfo(worldInfo.DirectoryName);
}