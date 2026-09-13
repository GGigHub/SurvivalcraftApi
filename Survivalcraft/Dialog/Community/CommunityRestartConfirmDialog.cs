using System;
using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityRestartConfirmDialog : Dialog
    {
        private readonly Action m_restartAction;
        private readonly Action m_restartAndContinueAction;
        private readonly BevelledButtonWidget m_restartButton;
        private readonly BevelledButtonWidget m_restartAndContinueButton;
        private readonly BevelledButtonWidget m_cancelButton;

        public CommunityRestartConfirmDialog(
            string title,
            string message,
            Action restartAction,
            Action restartAndContinueAction)
        {
            m_restartAction = restartAction;
            m_restartAndContinueAction = restartAndContinueAction;

            XElement node = ContentManager.Get<XElement>("Dialogs/ScbbsPlusRestartConfirmDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("ScbbsPlusRestartConfirmDialog.Title").Text =
                string.IsNullOrWhiteSpace(title) ? LanguageControl.Get("ScbbsPlusRestartConfirmDialog", "Text", "RestartRequired") : title;
            Children.Find<LabelWidget>("ScbbsPlusRestartConfirmDialog.Message").Text = message ?? string.Empty;
            m_restartButton = Children.Find<BevelledButtonWidget>("ScbbsPlusRestartConfirmDialog.RestartButton");
            m_restartAndContinueButton = Children.Find<BevelledButtonWidget>("ScbbsPlusRestartConfirmDialog.RestartAndContinueButton");
            m_cancelButton = Children.Find<BevelledButtonWidget>("ScbbsPlusRestartConfirmDialog.CancelButton");
            m_cancelButton.Text = LanguageControl.Cancel;
            m_restartAndContinueButton.IsVisible = m_restartAndContinueAction != null;
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
                m_restartAction?.Invoke();
                return;
            }

            if (m_restartAndContinueButton.IsClicked)
            {
                DialogsManager.HideDialog(this);
                m_restartAndContinueAction?.Invoke();
            }
        }
    }
}
