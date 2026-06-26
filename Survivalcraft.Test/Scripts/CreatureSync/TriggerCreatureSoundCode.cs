var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) {
    return "missing_entity";
}

var sounds = entity.FindComponent<ComponentCreatureSounds>();
if (sounds == null) {
    return "missing_sounds";
}

sounds.m_idleSound = "Audio/Creatures/Duck";
sounds.m_painSound = "Audio/Creatures/Duck";
sounds.m_moanSound = "Audio/Creatures/MaleMoan";
sounds.m_sneezeSound = "Audio/Creatures/MaleSneeze";
sounds.m_coughSound = "Audio/Creatures/MaleCough";
sounds.m_pukeSound = "Audio/Creatures/MalePuke";
sounds.m_attackSound = "Audio/Creatures/Duck";
sounds.m_idleSoundMinDistance = 100f;
sounds.m_painSoundMinDistance = 100f;
sounds.m_moanSoundMinDistance = 100f;
sounds.m_sneezeSoundMinDistance = 100f;
sounds.m_coughSoundMinDistance = 100f;
sounds.m_pukeSoundMinDistance = 100f;
sounds.m_attackSoundMinDistance = 100f;
sounds.m_lastSoundTime = -1000.0;
sounds.m_lastCoughingSoundTime = -1000.0;
sounds.m_lastPukeSoundTime = -1000.0;

int soundType = __SOUND_TYPE__;
if (soundType == 0) {
    sounds.PlayIdleSound(false);
}
else if (soundType == 1) {
    sounds.PlayPainSound();
}
else if (soundType == 2) {
    sounds.PlayMoanSound();
}
else if (soundType == 3) {
    sounds.PlaySneezeSound();
}
else if (soundType == 4) {
    sounds.PlayCoughSound();
}
else if (soundType == 5) {
    sounds.PlayPukeSound();
}
else if (soundType == 6) {
    sounds.PlayAttackSound();
}
else {
    return "unknown_type";
}

return string.Format(System.Globalization.CultureInfo.InvariantCulture, "ok:{0}", soundType);
