var project = Game.GameManager.Project;
if (project == null) {
    return "missing";
}

var gameInfo = project.FindSubsystem<Game.SubsystemGameInfo>();
var weather = project.FindSubsystem<Game.SubsystemWeather>();
gameInfo.WorldSettings.AreSeasonsChanging = false;
gameInfo.WorldSettings.TimeOfYear = __TIME_OF_YEAR__;
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "time={0:R};offset={1:R};timeOfYear={2:R};precipStart={3:R};precipEnd={4:R};lightning={5:R};fogStart={6:R};fogEnd={7:R};fogRamp={8:R}",
    gameInfo.TotalElapsedGameTime,
    gameInfo.TimeOfDay.TimeOfDayOffset,
    gameInfo.WorldSettings.TimeOfYear,
    weather.m_precipitationStartTime,
    weather.m_precipitationEndTime,
    weather.m_lightningIntensity,
    weather.m_fogStartTime,
    weather.m_fogEndTime,
    weather.m_fogRampTime);
