var playerData = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.IsMainPlayer);
var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
if (inventory == null) return "missing";

for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.RemoveSlotItems(i, inventory.GetSlotCount(i));
}

inventory.AddSlotItems(10, Game.Terrain.MakeBlockValue(Game.PlanksBlock.Index), 1);
return "seeded";
