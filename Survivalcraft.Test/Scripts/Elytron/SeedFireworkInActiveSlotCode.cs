var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";
var inventory = player.ComponentMiner?.Inventory;
if (inventory == null) return "no-inventory";

int slot = inventory.ActiveSlotIndex;
if (slot < 0) return "no-active-slot";

inventory.SetSlotValue(slot, new Game.ComponentInventoryBase.Slot { Value = __FIREWORK_BLOCK__, Count = __COUNT__ });

// 重置模组内部的烟花计数 token，避免旧状态提前触发升空
var gliding = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentGliding");
if (gliding != null) {
    var t = gliding.GetType();
    var b = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var fActive = t.GetField("m_lastActiveIndex", b);
    var fCount = t.GetField("m_lastFireworksCount", b);
    if (fActive != null && fActive.FieldType == typeof(int)) fActive.SetValue(gliding, -1);
    if (fCount != null && fCount.FieldType == typeof(int)) fCount.SetValue(gliding, 0);
}

return "ok:active=" + inventory.ActiveSlotIndex
    + ":slotValue=" + inventory.GetSlotValue(slot)
    + ":slotCount=" + inventory.GetSlotCount(slot);