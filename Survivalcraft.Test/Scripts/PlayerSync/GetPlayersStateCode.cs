var project = GameManager.Project;
if (project == null) {
    return "no-project";
}

var subsystemPlayers = project.FindSubsystem<SubsystemPlayers>();
if (subsystemPlayers == null) {
    return "no-subsystem";
}

return string.Join("|", subsystemPlayers.PlayersData
    .OrderBy(p => p.PlayerIndex)
    .Select(p => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"index={p.PlayerIndex};name={p.Name};skin={p.CharacterSkinName};class={p.PlayerClass};isMain={p.IsMainPlayer};hasEntity={p.ComponentPlayer != null}")));
