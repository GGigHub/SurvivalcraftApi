var project = Game.GameManager.Project;
if (project == null) {
    return "missing";
}

var gameInfo = project.FindSubsystem<Game.SubsystemGameInfo>();
var weather = project.FindSubsystem<Game.SubsystemWeather>();
var sky = project.FindSubsystem<Game.SubsystemSky>();
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "time={0:R};offset={1:R};timeOfYear={2:R};precipStart={3:R};precipEnd={4:R};lightning={5:R};fogStart={6:R};fogEnd={7:R};fogRamp={8:R};precipStarted={9};fogStarted={10};lastLightning={11:R};lightningX={12:R};lightningY={13:R};lightningZ={14:R};lightningBrightness={15:R}",
    gameInfo.TotalElapsedGameTime,
    gameInfo.TimeOfDay.TimeOfDayOffset,
    gameInfo.WorldSettings.TimeOfYear,
    weather.m_precipitationStartTime,
    weather.m_precipitationEndTime,
    weather.m_lightningIntensity,
    weather.m_fogStartTime,
    weather.m_fogEndTime,
    weather.m_fogRampTime,
    weather.IsPrecipitationStarted,
    weather.IsFogStarted,
    sky.m_lastLightningStrikeTime,
    sky.m_lightningStrikePosition?.X ?? float.NaN,
    sky.m_lightningStrikePosition?.Y ?? float.NaN,
    sky.m_lightningStrikePosition?.Z ?? float.NaN,
    sky.m_lightningStrikeBrightness);
