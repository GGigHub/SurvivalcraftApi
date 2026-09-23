using Engine;
using GameEntitySystem;

namespace Game {
    public class TetrisMenuEntryWidget : CanvasWidget {
        public BevelledButtonWidget Button;

        public TetrisMenuEntryWidget() {
            Button = new BevelledButtonWidget {
                Text = Tetris3DScreen.T("MenuButton"),
                Size = new Vector2(200f, 52f),
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center,
                Margin = new Vector2(0f, 4f)
            };
            Children.Add(Button);
        }

        public override void Update() {
            if (Button.IsClicked) {
                ScreensManager.SwitchScreen("Tetris3D");
            }
        }
    }

    public class TetrisModLoader : ModLoader {
        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);
            ModsManager.RegisterHook("OnMainMenuScreenCreated", this);
            ModsManager.RegisterHook("OnPlayerInputInteract", this);
        }

        public override void OnLoadingFinished(List<Action> actions) {
            actions.Add(() => {
                if (ScreensManager.FindScreen<Screen>("Tetris3D") == null) {
                    ScreensManager.AddScreen("Tetris3D", new Tetris3DScreen());
                }
            });
        }

        public override void OnMainMenuScreenCreated(
            MainMenuScreen mainMenuScreen,
            StackPanelWidget leftBottomBar,
            StackPanelWidget rightBottomBar) {
            if (rightBottomBar == null) {
                return;
            }
            rightBottomBar.Children.Add(new TetrisMenuEntryWidget());
        }

        public override void OnPlayerInputInteract(ComponentPlayer componentPlayer,
            ref bool playerOperated,
            ref double timeIntervalLastActionTime,
            ref int priorityUse,
            ref int priorityInteract,
            ref int priorityPlace) {
            Project project = GameManager.Project;
            if (project == null) {
                return;
            }
            SubsystemTetrisArcade arcade = project.FindSubsystem<SubsystemTetrisArcade>();
            if (arcade != null && arcade.IsPlayerOccupying(componentPlayer)) {
                priorityUse = 0;
                priorityPlace = 0;
            }
        }
    }
}
