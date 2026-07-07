var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var player = project
    .FindSubsystem<Game.SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);

if (player == null) return "missing-player";

int x = __X__;
int y = __Y__;
int z = __Z__;
int value = terrain.Terrain.GetCellValue(x, y, z);
int contents = Game.Terrain.ExtractContents(value);
string kind = "__KIND__";

switch (kind) {
    case "AdjustableDelayGate": {
        if (contents != Game.AdjustableDelayGateBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemAdjustableDelayGateBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditAdjustableDelayGateDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.Dismiss(__EDIT_VALUE__);
        break;
    }
    case "Battery": {
        if (contents != Game.BatteryBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemBatteryBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditVoltageLevelDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.Dismiss(__EDIT_VALUE__);
        break;
    }
    case "Button": {
        if (contents != Game.ButtonBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemButtonBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditVoltageLevelDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.Dismiss(__EDIT_VALUE__);
        break;
    }
    case "Switch": {
        if (contents != Game.SwitchBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemSwitchBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditVoltageLevelDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.Dismiss(__EDIT_VALUE__);
        break;
    }
    case "Piston": {
        if (contents != Game.PistonBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemPistonBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditPistonDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.Dismiss(__EDIT_VALUE__);
        break;
    }
    default:
        return "unknown-kind";
}

return "edited";
