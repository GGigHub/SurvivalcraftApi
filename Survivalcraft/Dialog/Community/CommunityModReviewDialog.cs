using System;
using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityModReviewDialog : Dialog
    {
        private readonly StarRatingWidget m_starRating;
        private readonly TextBoxWidget m_textBox;
        private readonly LinkWidget m_restoreLink;
        private readonly LabelWidget m_statusLabel;
        private readonly ButtonWidget m_submitButton;
        private readonly ButtonWidget m_cancelButton;
        private readonly Action<int, string> m_submit;
        private readonly string m_lastText;

        public CommunityModReviewDialog(
            string modName,
            int rating,
            string text,
            string lastText,
            Action<int, string> submit,
            string dialogTitle = null)
        {
            m_submit = submit;
            m_lastText = lastText ?? string.Empty;

            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityModReviewDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("ModReviewDialog.Title").Text =
                string.IsNullOrWhiteSpace(dialogTitle) ? LanguageControl.Get("ModReviewDialog", "ReviewLocalResource") : dialogTitle;
            Children.Find<LabelWidget>("ModReviewDialog.ModNameLabel").Text =
                string.IsNullOrWhiteSpace(modName) ? LanguageControl.Get("ModReviewDialog", "LocalResource") : modName;
            m_starRating = Children.Find<StarRatingWidget>("ModReviewDialog.StarRating");
            m_restoreLink = Children.Find<LinkWidget>("ModReviewDialog.RestoreLink");
            m_textBox = Children.Find<TextBoxWidget>("ModReviewDialog.TextBox");
            m_statusLabel = Children.Find<LabelWidget>("ModReviewDialog.StatusLabel");
            m_submitButton = Children.Find<ButtonWidget>("ModReviewDialog.SubmitButton");
            m_cancelButton = Children.Find<ButtonWidget>("ModReviewDialog.CancelButton");
            m_cancelButton.Text = LanguageControl.Cancel;
            m_textBox.Text = text ?? string.Empty;
            m_textBox.TextChanged += _ => UpdateSubmitState();
            m_starRating.Rating = Math.Clamp(rating, 0, 5);
            m_textBox.HasFocus = true;
            UpdateSubmitState();
        }

        public override void Update()
        {
            UpdateSubmitState();

            if (m_restoreLink.IsClicked)
            {
                // 无历史内容时不动当前草稿，只提示。
                if (string.IsNullOrEmpty(m_lastText))
                {
                    m_statusLabel.Text = LanguageControl.Get("ModReviewDialog", "ThereIsNoPreviousContentToRestore");
                }
                else
                {
                    m_textBox.Text = m_lastText;
                    m_statusLabel.Text = LanguageControl.Get("ModReviewDialog", "PreviousContentRestored");
                }
                m_textBox.HasFocus = true;
            }

            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))
                || m_cancelButton.IsClicked)
            {
                DialogsManager.HideDialog(this);
                return;
            }

            // Input.Ok（手柄 A/VR 扳机）与按钮同受评分门槛约束：0 星不允许提交。
            if ((Input.Ok || m_submitButton.IsClicked) && m_starRating.Rating > 0f)
            {
                DialogsManager.HideDialog(this);
                m_submit?.Invoke((int)m_starRating.Rating, m_textBox.Text ?? string.Empty);
            }
        }

        private void UpdateSubmitState()
        {
            m_submitButton.IsEnabled = m_starRating.Rating > 0f;
            if (m_starRating.Rating <= 0f)
            {
                m_statusLabel.Text = LanguageControl.Get("ModReviewDialog", "PleaseSelectARating");
            }
            else if (m_statusLabel.Text == LanguageControl.Get("ModReviewDialog", "PleaseSelectARating"))
            {
                m_statusLabel.Text = string.Empty;
            }
        }
    }
}
