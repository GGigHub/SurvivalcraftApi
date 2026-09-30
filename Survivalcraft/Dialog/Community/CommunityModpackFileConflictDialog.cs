using System;
using System.Globalization;
using System.Xml.Linq;

namespace Game
{
    public enum ModpackFileConflictAction
    {
        Overwrite,
        Skip,
        SkipAll,
        OverwriteAll,
        Close
    }

    public sealed class CommunityModpackFileConflictDialog : Dialog
    {
        private readonly ButtonWidget m_overwriteButton;
        private readonly ButtonWidget m_skipButton;
        private readonly ButtonWidget m_skipAllButton;
        private readonly ButtonWidget m_overwriteAllButton;
        private readonly ButtonWidget m_closeButton;
        private readonly Action<ModpackFileConflictAction> m_handler;

        public CommunityModpackFileConflictDialog(string fileName, Action<ModpackFileConflictAction> handler)
        {
            m_handler = handler;

            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityModpackFileConflictDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("ScbbsPlusModpackFileConflictDialog.Message").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackFileConflictDialog", "IdenticalFileChooseAction"), fileName);
            m_overwriteButton = Children.Find<ButtonWidget>("ScbbsPlusModpackFileConflictDialog.OverwriteButton");
            m_skipButton = Children.Find<ButtonWidget>("ScbbsPlusModpackFileConflictDialog.SkipButton");
            m_skipAllButton = Children.Find<ButtonWidget>("ScbbsPlusModpackFileConflictDialog.SkipAllButton");
            m_overwriteAllButton = Children.Find<ButtonWidget>("ScbbsPlusModpackFileConflictDialog.OverwriteAllButton");
            m_closeButton = Children.Find<ButtonWidget>("ScbbsPlusModpackFileConflictDialog.CloseButton");
        }

        public override void Update()
        {
            if (m_closeButton.IsClicked
                || Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                Complete(ModpackFileConflictAction.Close);
            }
            else if (m_overwriteButton.IsClicked)
            {
                Complete(ModpackFileConflictAction.Overwrite);
            }
            else if (m_skipButton.IsClicked)
            {
                Complete(ModpackFileConflictAction.Skip);
            }
            else if (m_skipAllButton.IsClicked)
            {
                Complete(ModpackFileConflictAction.SkipAll);
            }
            else if (m_overwriteAllButton.IsClicked)
            {
                Complete(ModpackFileConflictAction.OverwriteAll);
            }
        }

        private void Complete(ModpackFileConflictAction action)
        {
            DialogsManager.HideDialog(this);
            m_handler?.Invoke(action);
        }
    }
}
