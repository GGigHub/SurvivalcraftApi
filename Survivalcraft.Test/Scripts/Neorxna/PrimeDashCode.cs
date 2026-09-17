var project = Game.GameManager.Project;
if (project == null) return "no-project";

var pd = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player == null) return "no-player";

var dash = player.Entity.FindComponent<Neorxna.Components.ComponentDash>();
if (dash == null) return "no-dash";

// 触发模组自带的"双击前进"冲刺判定，无需物理键盘：
// 下一次 Move.Z > 0 的帧里，ComponentDash.Update 的双击分支会置 IsDashing=true，
// 并走真实的冲刺消耗/加速逻辑。
dash.m_pressed = true;
dash.m_released = true;
dash.m_lastForwardTime = Engine.Time.RealTime - 0.1;
return "primed";
