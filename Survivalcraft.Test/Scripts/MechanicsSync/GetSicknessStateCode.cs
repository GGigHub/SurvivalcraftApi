var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var sickness = player?.ComponentSickness;
if (sickness == null) return "missing";
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "sick={0};nausea={1};duration={2:R}", sickness.IsSick ? 1 : 0, sickness.m_lastNauseaTime.HasValue ? 1 : 0, sickness.m_sicknessDuration);
