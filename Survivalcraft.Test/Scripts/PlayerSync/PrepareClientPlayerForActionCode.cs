var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player?.ComponentMiner?.Inventory == null) return "missing";

var inventory = player.ComponentMiner.Inventory;
inventory.ActiveSlotIndex = 0;
var emptySlot = new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 };
for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.SetSlotValue(i, emptySlot);
}
inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot {
    Value = __ACTIVE_VALUE__,
    Count = __ACTIVE_COUNT__
});

var body = player.ComponentBody;
body.Position = new Engine.Vector3(__BODY_X__f, __BODY_Y__f, __BODY_Z__f);
body.Rotation = Engine.Quaternion.Identity;
body.Velocity = Engine.Vector3.Zero;
body.IsGravityEnabled = false;
body.IsSmoothRiseEnabled = false;
player.ComponentLocomotion.IsCreativeFlyEnabled = true;
player.ComponentLocomotion.LookAngles = Engine.Vector2.Zero;
player.m_lastActionTime = -1000.0;
player.Update(0f);
player.ComponentCreatureModel.Update(0f);
player.GameWidget.ActiveCamera.Update(0f);
return "ok";
