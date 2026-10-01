using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    internal sealed class CommunityToastWidget : CanvasWidget
    {
        private const float HiddenBottomMargin = -48f;
        private const float VisibleBottomMargin = 40f;
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
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityToastWidget");
            LoadContents(this, node);
            m_text = Children.Find<LabelWidget>("ToastWidget.Text");
            m_confirm = Children.Find<LinkWidget>("ToastWidget.Confirm");
            m_close = Children.Find<LinkWidget>("ToastWidget.Close");
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
            ReserveSpaceForLinks();
            m_isActive = true;
            m_shownTime = Time.RealTime;
            IsVisible = true;
            Margin = new Vector2(0f, ComputeHiddenMargin());
        }

        // 文本换行宽度来自面板可用宽度（LabelWidget 自身 Size 不约束换行），
        // 用右边距为动作链接预留实际占宽，长文本才不会被链接覆盖到
        private void ReserveSpaceForLinks()
        {
            float reserved = 8f;
            if (m_confirm.IsVisible) {
                reserved += 40f + MeasureLinkWidth(m_confirm);
            }
            if (m_close.IsVisible) {
                reserved += MeasureLinkWidth(m_close) + 4f;
            }
            m_text.MarginRight = reserved;
        }

        private static float MeasureLinkWidth(LinkWidget link)
        {
            return link.Font.MeasureText(link.Text ?? string.Empty, new Vector2(link.FontScale), Vector2.Zero).X;
        }

        // 多行时 48px 不足以完全滑出屏幕外，隐藏位按当前高度取
        private float ComputeHiddenMargin()
        {
            return -MathUtils.Max(-HiddenBottomMargin, ActualSize.Y + VisibleBottomMargin + 8f);
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
            float hiddenMargin = ComputeHiddenMargin();
            Margin = new Vector2(0f, hiddenMargin + (VisibleBottomMargin - hiddenMargin) * t);

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
            Margin = new Vector2(0f, ComputeHiddenMargin());
        }
    }
}
