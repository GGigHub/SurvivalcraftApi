var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var sickness = player?.ComponentSickness;
if (sickness == null) return "missing";
player.ComponentHealth.Health = System.Math.Max(player.ComponentHealth.Health, 0.5f);
if (sickness.m_sicknessDuration <= 0f) sickness.StartSickness();
sickness.NauseaEffect();
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "sick={0};nausea={1};duration={2:R}", sickness.IsSick ? 1 : 0, sickness.m_lastNauseaTime.HasValue ? 1 : 0, sickness.m_sicknessDuration);
