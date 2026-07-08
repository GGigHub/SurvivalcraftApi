var project = Game.GameManager.Project;
if (project == null) {
    return "missing";
}

var player = project.FindSubsystem<Game.SubsystemPlayers>()?
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player?.ComponentGui == null || player.ComponentInput == null) {
    return "missing-player";
}

switch ("__ACTION__") {
    case "TimeOfDay":
        player.ComponentInput.m_playerInput.TimeOfDay = true;
        break;
    case "Lightning":
        player.ComponentInput.m_playerInput.Lighting = true;
        break;
    case "Precipitation":
        player.ComponentInput.m_playerInput.Precipitation = true;
        break;
    case "Fog":
        player.ComponentInput.m_playerInput.Fog = true;
        break;
    default:
        return "unknown-action";
}

player.ComponentGui.HandleInput();
return "__ACTION__";
