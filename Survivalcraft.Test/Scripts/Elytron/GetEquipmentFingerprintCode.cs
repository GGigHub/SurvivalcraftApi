var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";

var clothing = player.ComponentClothing;
string torso = clothing == null ? "no-clothing" : string.Join(",", clothing.GetClothes(Game.ClothingSlot.Torso));

string acc = "no-accessory-component";
var accessory = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentElytraAccessory");
if (accessory != null) {
    string has322 = "false";
    var hasMethod = accessory.GetType().GetMethod("HasAccessory");
    if (hasMethod != null) {
        object r = hasMethod.Invoke(accessory, new object[] { 322 });
        has322 = r != null ? r.ToString() : "null";
    }
    int count = 0;
    string values = "";
    var field = accessory.GetType().GetField("m_accessories", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (field != null) {
        var list = field.GetValue(accessory) as System.Collections.IEnumerable;
        if (list != null) {
            foreach (var item in list) {
                count++;
                var bvField = item.GetType().GetField("BlockValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                object bv = bvField != null ? bvField.GetValue(item) : null;
                values += (values.Length == 0 ? "" : ",") + (bv != null ? bv.ToString() : "?");
            }
        }
    }
    acc = "has322=" + has322 + ";count=" + count + ";values=[" + values + "]";
}

return "torso=[" + torso + "];acc=" + acc;