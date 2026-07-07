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

return "ok";

