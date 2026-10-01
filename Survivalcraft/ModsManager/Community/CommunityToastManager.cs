using System;

namespace Game
{
    internal static class CommunityToastManager
    {
        private static CommunityToastWidget s_widget;
        private static Screen s_screen;

        public static bool IsActive => s_widget?.IsActive == true;

        public static void DetachFromScreen(Screen screen)
        {
            DetachFromScreen(screen, invokeDismiss: true);
        }

        public static void Show(string message, Action confirmAction = null, Action closeAction = null, bool showClose = true, double autoHideSeconds = 0.0, string confirmText = null, string closeText = null, Action dismissAction = null)
        {
            EnsureWidget();
            Screen currentScreen = ScreensManager.CurrentScreen;
            if (currentScreen != null
                && (!ReferenceEquals(s_screen, currentScreen) || !ReferenceEquals(s_widget.ParentWidget, currentScreen)))
            {
                DetachFromScreen(s_screen, invokeDismiss: false);
                s_screen = currentScreen;
                currentScreen.Children.Add(s_widget);
            }
            s_widget.Show(message, confirmAction, closeAction, showClose, autoHideSeconds, confirmText, closeText, dismissAction);
        }

        public static void Hide()
        {
            s_widget?.Hide();
        }

        private static void EnsureWidget()
        {
            s_widget ??= new CommunityToastWidget();
        }

        private static void DetachFromScreen(Screen screen, bool invokeDismiss)
        {
            if (screen == null || !ReferenceEquals(s_screen, screen))
            {
                return;
            }

            if (invokeDismiss)
            {
                s_widget?.Hide();
            }
            else
            {
                s_widget?.Reset();
            }

            if (ReferenceEquals(s_widget?.ParentWidget, screen))
            {
                screen.Children.Remove(s_widget);
            }

            s_screen = null;
        }
    }
}
