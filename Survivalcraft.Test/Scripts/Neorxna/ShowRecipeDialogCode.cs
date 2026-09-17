var project = Game.GameManager.Project;
if (project == null) return "no-project";

var main = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.IsMainPlayer)?.ComponentPlayer;
if (main == null) return "no-main-player";
if (main.GuiWidget == null) return "no-gui";

var dialog = new Neorxna.NUI.NUIRecipaediaRecipesDialog(new List<Game.Widget> { new Game.CraftingRecipeWidget() });
DialogsManager.ShowDialog(main.GuiWidget, dialog);
return "shown";
