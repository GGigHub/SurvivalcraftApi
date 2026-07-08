var project = GameManager.Project;
if (project == null) {
    return "no-project";
}

var subsystemPlayers = project.FindSubsystem<SubsystemPlayers>();
var playerData = subsystemPlayers?.PlayersData.FirstOrDefault(p => p.IsMainPlayer);
if (playerData == null) {
    return "no-main-player";
}

playerData.Name = "__PLAYER_NAME__";

CharacterSkinsManager.UpdateCharacterSkinsList();
var playerClass = playerData.PlayerClass;
var nextSkin = CharacterSkinsManager.CharacterSkinsNames.FirstOrDefault(n =>
    n != playerData.CharacterSkinName
    && (CharacterSkinsManager.GetPlayerClass(n) == playerClass || CharacterSkinsManager.GetPlayerClass(n) == null));
if (!string.IsNullOrEmpty(nextSkin)) {
    playerData.CharacterSkinName = nextSkin;
}

var eventField = typeof(PlayerScreen).GetField(
    "OnPlayerModify",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var handler = eventField?.GetValue(null) as Action<PlayerData>;
handler?.Invoke(playerData);

return string.Create(
    System.Globalization.CultureInfo.InvariantCulture,
    $"index={playerData.PlayerIndex};name={playerData.Name};skin={playerData.CharacterSkinName};class={playerData.PlayerClass};event={(handler != null)}");
