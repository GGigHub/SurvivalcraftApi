int playerIndex = __PLAYER_INDEX__;
var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == playerIndex);
if (playerData == null || playerData.ComponentPlayer == null) {
    return "no-player";
}

var clothingMass = playerData.ComponentPlayer.Entity.FindComponent<SAGhoul.Components.ComponentClothingMass>();
if (clothingMass == null) {
    return "no-clothing-mass-component";
}

return string.Join(";",
    "armorProtection=" + clothingMass.ArmorProtection.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "hasFullSet=" + clothingMass.HasFullSet,
    "recoveryTo1Time=" + clothingMass.RecoveryTo1Time.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "isAcArmorEquipped=" + clothingMass.IsAcArmorEquipped());