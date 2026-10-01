using System;
using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    // 重启确认对话框：按钮回调返回 true 时由对话框执行对应重启，返回 false 仅关闭窗口。
    public sealed class RestartConfirmDialog : Dialog
    {
        private readonly Func<bool> m_restartAction;
        private readonly Func<bool> m_restartToCurrentScreenAction;
        private readonly BevelledButtonWidget m_restartButton;
        private readonly BevelledButtonWidget m_restartToCurrentScreen;
        private readonly BevelledButtonWidget m_cancelButton;

        public RestartConfirmDialog(
            string title,
            string message,
            Func<bool> restartAction,
            Func<bool> restartToCurrentScreenAction)
        {
            m_restartAction = restartAction;
            m_restartToCurrentScreenAction = restartToCurrentScreenAction;

            XElement node = ContentManager.Get<XElement>("Dialogs/RestartConfirmDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("RestartConfirmDialog.Title").Text =
                string.IsNullOrWhiteSpace(title) ? LanguageControl.Get("RestartConfirmDialog", "RestartRequired") : title;
            Children.Find<LabelWidget>("RestartConfirmDialog.Message").Text = message ?? string.Empty;
            m_restartButton = Children.Find<BevelledButtonWidget>("RestartConfirmDialog.RestartButton");
            m_restartToCurrentScreen = Children.Find<BevelledButtonWidget>("RestartConfirmDialog.RestartToCurrentScreen");
            m_cancelButton = Children.Find<BevelledButtonWidget>("RestartConfirmDialog.CancelButton");
            m_cancelButton.Text = LanguageControl.Cancel;
            m_restartToCurrentScreen.IsVisible = m_restartToCurrentScreenAction != null;
        }

        /// <summary>
        /// 模组导入完成后的统一重启提示：两按钮回调先兑现挂起的本地模组动作清单
        /// （PendingLocalModActions）并返回 true，重启本身由对话框执行。
        /// reasonTag 仅用于日志区分调用链路（如 "remote-import"、"import"）。
        /// </summary>
        public static void ShowModImportRestartConfirm(string fileName, string reasonTag)
        {
            DialogsManager.ShowDialog(
                null,
                new RestartConfirmDialog(
                    LanguageControl.Get("RestartConfirmDialog", "UpdateComplete"),
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RestartConfirmDialog", "ImportedRestartPrompt"), fileName),
                    restartAction: () =>
                    {
                        PendingLocalModActions.Process(reasonTag + "-restart");
                        return true;
                    },
                    restartToCurrentScreenAction: () =>
                    {
                        PendingLocalModActions.Process(reasonTag + "-restart-continue");
                        return true;
                    }));
        }

        public override void Update()
        {
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))
                || m_cancelButton.IsClicked)
            {
                DialogsManager.HideDialog(this);
                return;
            }

            if (Input.Ok || m_restartButton.IsClicked)
            {
                DialogsManager.HideDialog(this);
                if (m_restartAction?.Invoke() == true)
                {
                    Window.Restart();
                }
                return;
            }

            if (m_restartToCurrentScreen.IsClicked)
            {
                DialogsManager.HideDialog(this);
                if (m_restartToCurrentScreenAction?.Invoke() == true)
                {
                    ScreensManager.RestartGameToCurrentScreen();
                }
            }
        }
    }
}
