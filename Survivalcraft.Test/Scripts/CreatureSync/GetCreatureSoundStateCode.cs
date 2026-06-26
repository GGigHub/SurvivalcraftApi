var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) {
    return "missing_entity";
}

var sounds = entity.FindComponent<ComponentCreatureSounds>();
if (sounds == null) {
    return "missing_sounds";
}

int soundType = __SOUND_TYPE__;
double value = soundType == 4
    ? sounds.m_lastCoughingSoundTime
    : soundType == 5
        ? sounds.m_lastPukeSoundTime
        : sounds.m_lastSoundTime;

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "type={0};played={1};last={2:R};cough={3:R};puke={4:R}",
    soundType,
    value > -999.0 ? 1 : 0,
    sounds.m_lastSoundTime,
    sounds.m_lastCoughingSoundTime,
    sounds.m_lastPukeSoundTime);
