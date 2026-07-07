var project = Game.GameManager.Project;
var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
var player = project
    .FindSubsystem<Game.SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player?.ComponentMiner == null) return "missing-player";

var blockEntity = blockEntities.GetBlockEntity(__X__, __Y__, __Z__);
if (blockEntity == null) return "missing-entity";

var dispenser = blockEntity.Entity.FindComponent<Game.ComponentDispenser>();
if (dispenser == null) return "missing-dispenser";

var widget = new Game.DispenserWidget(player.ComponentMiner.Inventory, dispenser);
((Game.BevelledButtonWidget)widget.m_shootButton).m_clickableWidget.IsClicked = true;
widget.m_acceptsDropsBox.m_clickableWidget.IsClicked = true;
widget.Update();

return "edited";
