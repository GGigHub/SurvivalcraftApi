foreach (var setting in Neorxna.NeorxnaSettingOptions.SettingConfigs.Values) {
    if (setting.Type == typeof(bool)) {
        Neorxna.NeorxnaSettingOptions.Settings.SetValue<object>(setting.Key, true);
    }
}

var playerData = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.IsMainPlayer);
var modal = playerData?.ComponentPlayer?.ComponentGui?.ModalPanelWidget as Game.ContainerWidget;
if (modal == null) return "modal_missing";

var slot = modal.AllChildren
    .OfType<Game.InventorySlotWidget>()
    .FirstOrDefault(s => s.m_inventory != null && s.m_inventory.GetSlotCount(s.m_slotIndex) > 0);
if (slot == null) return "slot_missing";

var center = slot.GlobalBounds.Center();
return "armed:" + slot.m_slotIndex.ToString() + ":" + ((int)center.X).ToString() + ":" + ((int)center.Y).ToString();
