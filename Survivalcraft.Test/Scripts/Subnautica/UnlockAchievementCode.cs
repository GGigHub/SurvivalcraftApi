var manager = Subnautica.AchievementManager.Current;
if (manager == null) return "no-manager";
manager.Report(new Subnautica.AchievementEvent(Subnautica.AchievementEventType.KillCreature, __PLAYER_ENTITY_ID__, "Eel", 1));
return "reported";