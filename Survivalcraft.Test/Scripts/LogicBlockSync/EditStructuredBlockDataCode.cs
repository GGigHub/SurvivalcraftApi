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
var point = new Engine.Point3(x, y, z);
string kind = "__KIND__";

switch (kind) {
    case "MemoryBank": {
        if (contents != Game.MemoryBankBlock.Index) return "wrong-block";
        Game.SettingsManager.UsePrimaryMemoryBank = true;
        var behavior = project.FindSubsystem<Game.SubsystemMemoryBankBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditMemoryBankDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.m_memoryBankData.LoadString("__MEMORY_DATA__");
        dialog.Dismiss(true);
        break;
    }
    case "TruthTable": {
        if (contents != Game.TruthTableCircuitBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemTruthTableCircuitBlockBehavior>();
        if (!behavior.OnEditBlock(x, y, z, value, player)) return "edit-rejected";
        var dialog = Game.DialogsManager.Dialogs.OfType<Game.EditTruthTableDialog>().LastOrDefault();
        if (dialog == null) return "missing-dialog";
        dialog.m_truthTableData.LoadString("__TRUTH_DATA__");
        dialog.Dismiss(true);
        break;
    }
    case "Sign": {
        if (contents != Game.WoodenPostedSignBlock.Index) return "wrong-block";
        var behavior = project.FindSubsystem<Game.SubsystemSignBlockBehavior>();
        behavior.SetSignData(
            point,
            ["__SIGN_LINE_1__", "__SIGN_LINE_2__", "__SIGN_LINE_3__", "__SIGN_LINE_4__"],
            [
                new Engine.Color(255, 0, 0),
                new Engine.Color(0, 255, 0),
                new Engine.Color(0, 0, 255),
                new Engine.Color(255, 255, 255)
            ],
            "__SIGN_URL__",
            sync: true);
        break;
    }
    default:
        return "unknown-kind";
}

return "edited";
