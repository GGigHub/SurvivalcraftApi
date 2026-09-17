var project = Game.GameManager.Project;
if (project == null) return "no-project";

var pd = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player == null) return "no-player";

var dash = player.Entity.FindComponent<Neorxna.Components.ComponentDash>();
var vital = player.ComponentVitalStats;
var factors = player.Entity.FindComponent<Game.ComponentLevel>();

string factorStr = factors == null
    ? string.Empty
    : string.Join(",", factors.m_speedFactors
        .Select(f => f.Name + "=" + string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R}", f.Value))
        .OrderBy(s => s));

string settingDump = string.Join(",", Neorxna.NeorxnaSettingOptions.SettingConfigs.Values
    .Where(s => s.Type == typeof(bool))
    .Select(s => s.Key + "=" + Neorxna.NeorxnaSettingOptions.Settings.GetValue<bool>(s.Key, false))
    .OrderBy(s => s));

string move = player.ComponentInput?.PlayerInput.Move.ToString() ?? "null";

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "dashing={0};enabled={1};food={2:R};stamina={3:R};factors={4};move={5};windowActive={6};keyF={7};bools=[{8}]",
    dash != null && dash.IsDashing,
    dash != null && dash.IsEnabled,
    vital != null ? vital.Food : -1f,
    vital != null ? vital.Stamina : -1f,
    factorStr,
    move,
    Engine.Window.IsActive,
    Engine.Input.Keyboard.IsKeyDown(Engine.Input.Key.F),
    settingDump);