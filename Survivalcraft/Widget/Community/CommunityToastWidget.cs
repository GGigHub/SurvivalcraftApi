using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    internal sealed class CommunityToastWidget : CanvasWidget
    {
        private const float HiddenBottomMargin = -48f;
        private const float VisibleBottomMargin = 18f;
        private const double AnimationDuration = 0.28;

        private readonly LabelWidget m_text;
        private readonly LinkWidget m_confirm;
        private readonly LinkWidget m_close;
        private bool m_isActive;
        private double m_shownTime;
        private Action m_confirmAction;
        private Action m_closeAction;
        private Action m_dismissAction;
        private double m_autoHideSeconds;

        public bool IsActive => m_isActive;

        public CommunityToastWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/ScbbsPlusToastWidget");
            LoadContents(this, node);
            m_text = Children.Find<LabelWidget>("ScbbsPlusToastWidget.Text");
            m_confirm = Children.Find<LinkWidget>("ScbbsPlusToastWidget.Confirm");
            m_close = Children.Find<LinkWidget>("ScbbsPlusToastWidget.Close");
            Margin = new Vector2(0f, HiddenBottomMargin);
            IsVisible = false;
        }

        public void Show(string message, Action confirmAction = null, Action closeAction = null, bool showClose = true, double autoHideSeconds = 0.0, string confirmText = null, string closeText = null, Action dismissAction = null)
        {
            if (m_isActive)
            {
                Hide();
            }

            m_text.Text = message ?? string.Empty;
            m_confirmAction = confirmAction;
            m_closeAction = closeAction;
            m_dismissAction = dismissAction;
            m_autoHideSeconds = autoHideSeconds;
            m_confirm.Text = confirmText ?? LanguageControl.Yes;
            m_close.Text = closeText ?? LanguageControl.Cancel;
            m_confirm.IsVisible = confirmAction != null;
            m_close.IsVisible = showClose || closeAction != null;
            m_isActive = true;
            m_shownTime = Time.RealTime;
            IsVisible = true;
            Margin = new Vector2(0f, HiddenBottomMargin);
        }

        public void Hide()
        {
            Action dismissAction = m_dismissAction;
            ClearState();
            dismissAction?.Invoke();
        }

        public void Reset()
        {
            ClearState();
        }

        public override void Update()
        {
            if (!m_isActive)
            {
                return;
            }

            double elapsed = Math.Max(0.0, Time.RealTime - m_shownTime);
            float t = (float)Math.Clamp(elapsed / AnimationDuration, 0.0, 1.0);
            t = 1f - (1f - t) * (1f - t);
            Margin = new Vector2(0f, HiddenBottomMargin + (VisibleBottomMargin - HiddenBottomMargin) * t);

            if (m_autoHideSeconds > 0.0 && elapsed >= m_autoHideSeconds)
            {
                Action closeAction = m_closeAction;
                Hide();
                closeAction?.Invoke();
                return;
            }

            if (m_close.IsClicked)
            {
                Action closeAction = m_closeAction;
                Hide();
                closeAction?.Invoke();
                return;
            }

            if (m_confirm.IsClicked)
            {
                Action confirmAction = m_confirmAction;
                Hide();
                confirmAction?.Invoke();
            }
        }

        private void ClearState()
        {
            m_isActive = false;
            m_confirmAction = null;
            m_closeAction = null;
            m_dismissAction = null;
            m_autoHideSeconds = 0.0;
            m_confirm.IsVisible = false;
            m_close.IsVisible = false;
            IsVisible = false;
            Margin = new Vector2(0f, HiddenBottomMargin);
        }
    }
}
