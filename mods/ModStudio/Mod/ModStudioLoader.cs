using Engine;

namespace Game {
    public class ModStudioLoader : ModLoader {
        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);
            ModsManager.RegisterHook("OnMainMenuScreenCreated", this);
        }

        public override void OnLoadingFinished(List<Action> actions) {
            actions.Add(() => {
                if (ScreensManager.FindScreen<Screen>("ModStudio") == null) {
                    ScreensManager.AddScreen("ModStudio", new ModStudioScreen());
                }
                StudioSettings.Load();
                StudioHttpServer.Instance.Start();
            });
        }

        public override void OnMainMenuScreenCreated(MainMenuScreen mainMenuScreen,
            StackPanelWidget leftBottomBar,
            StackPanelWidget rightBottomBar) {
            if (rightBottomBar == null) {
                return;
            }
            rightBottomBar.Children.Add(new ModStudioMenuEntryWidget());
        }

        public override void ModDispose() {
            StudioHttpServer.Instance.Stop();
        }
    }
}
