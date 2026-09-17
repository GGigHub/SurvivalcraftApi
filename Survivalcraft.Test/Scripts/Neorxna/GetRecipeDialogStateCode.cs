var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData;
var main = players.FirstOrDefault(p => p.IsMainPlayer)?.ComponentPlayer;
var dialog = DialogsManager.Dialogs.LastOrDefault(d => d.GetType().FullName == "Neorxna.NUI.NUIRecipaediaRecipesDialog");
return string.Join(";", new object[] {
    main != null,
    main?.GuiWidget != null,
    dialog != null,
    dialog?.ParentWidget == main?.GuiWidget,
    dialog?.IsVisible == true,
    dialog?.IsVisibleGlobal == true,
    main?.GuiWidget?.IsVisibleGlobal == true
});