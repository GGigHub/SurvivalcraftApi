using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;

namespace Game
{
    public sealed class PostDetailsDialog : Dialog
    {
        private readonly Widget m_actionsRow;
        private readonly StackPanelWidget m_contentPanel;
        private readonly StackPanelWidget m_headerPanel;
        private readonly ButtonWidget m_primaryButton;
        private readonly LabelWidget m_titleLabel;
        private readonly LabelWidget m_translateStatusLabel;
        private readonly Action m_primaryAction;
        private readonly string m_baseUrl;
        // content 区只放服务端来源内容（HTML 或纯文本二选一）；header 区放本地结构化数据，
        // 不参与自动翻译——翻译只发送 m_contentHtml，重建时 header 原地不动。
        private readonly string m_contentHtml;
        private readonly string m_contentPlainText;
        // 内容构建：HAP DOM → widget 的遍历与图片纹理生命周期都在构建器里。
        private readonly PostDetailsContentBuilder m_contentBuilder;
        private Task<AutoTranslateResult> m_autoTranslateTask;
        private CancellationTokenSource m_autoTranslateCancellation;
        private int m_autoTranslateRequestId;

        public PostDetailsDialog(
            string title,
            string html,
            string baseUrl = null,
            string primaryButtonText = null,
            Action primaryAction = null,
            bool enableAutoTranslate = false,
            Action<StackPanelWidget> buildHeader = null,
            string contentPlainText = null)
        {
            m_baseUrl = baseUrl;
            m_primaryAction = primaryAction;
            m_contentHtml = html ?? string.Empty;
            m_contentPlainText = contentPlainText;
            XElement node = ContentManager.Get<XElement>("Dialogs/ScbbsPlusPostDetailsDialog");
            LoadContents(this, node);

            m_titleLabel = Children.Find<LabelWidget>("ScbbsPlusPostDetailsDialog.Title");
            m_titleLabel.Text = string.IsNullOrWhiteSpace(title) ? LanguageControl.Get("ScbbsPlusPostDetailsDialog", "Text", "PostDetails") : title;
            m_contentPanel = Children.Find<StackPanelWidget>("ScbbsPlusPostDetailsDialog.ContentPanel");
            m_headerPanel = Children.Find<StackPanelWidget>("ScbbsPlusPostDetailsDialog.HeaderPanel");
            m_actionsRow = Children.Find<Widget>("ScbbsPlusPostDetailsDialog.ActionsRow");
            m_primaryButton = Children.Find<ButtonWidget>("ScbbsPlusPostDetailsDialog.PrimaryButton");
            m_translateStatusLabel = Children.Find<LabelWidget>("ScbbsPlusPostDetailsDialog.TranslateStatus");
            m_contentBuilder = new PostDetailsContentBuilder(m_contentPanel, m_baseUrl);

            bool hasPrimaryAction = !string.IsNullOrWhiteSpace(primaryButtonText) && primaryAction != null;
            m_actionsRow.IsVisible = hasPrimaryAction;
            m_primaryButton.IsVisible = hasPrimaryAction;
            if (hasPrimaryAction)
            {
                m_primaryButton.Text = primaryButtonText;
            }

            if (buildHeader != null)
            {
                buildHeader(m_headerPanel);
                m_headerPanel.IsVisible = m_headerPanel.Children.Count > 0;
            }

            BuildContent();
            if (enableAutoTranslate)
            {
                StartAutoTranslate();
            }
        }

        public override void Update()
        {
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                DialogsManager.HideDialog(this);
                return;
            }

            if (m_primaryButton.IsClicked)
            {
                DialogsManager.HideDialog(this);
                m_primaryAction?.Invoke();
            }

            UpdateAutoTranslateTask();
        }

        public override void UpdateCeases()
        {
            // 框架移除对话框时调用（Widget.cs UpdateCeases）。构建器释放图片纹理句柄并标记已释放，
            // 晚到的图片下载回调据此就地释放新纹理，不再加入列表。
            m_contentBuilder.ReleaseTextures();
            // 取消在途翻译 HTTP 请求：对话框已移除，结果无消费方。
            m_autoTranslateCancellation?.Cancel();
            base.UpdateCeases();
        }

        private void BuildContent(string translatedHtml = null)
        {
            // 重建不释放纹理：已加载图片入纹理缓存供新内容复用（自动翻译重建
            // 不再让第一批纹理成孤儿、同图下载两次），对话框关闭时统一释放。
            // 只清 content 面板，header 区（本地数据）不受翻译重建影响。
            m_contentPanel.Children.Clear();

            string html = translatedHtml ?? m_contentHtml;
            bool appended = !string.IsNullOrWhiteSpace(html) && m_contentBuilder.AppendContent(html);
            if (!appended && !string.IsNullOrWhiteSpace(m_contentPlainText))
            {
                PostDetailsContentBuilder.AppendPlainText(m_contentPanel, m_contentPlainText);
                appended = true;
            }
            if (!appended)
            {
                m_contentBuilder.AppendParagraph(LanguageControl.Get("ScbbsPlusPostDetailsDialog", "Text", "NoDetails"));
            }
        }

        private void StartAutoTranslate()
        {
            string translateUrl = GetTranslateUrl();
            string targetLanguage = GetSystemTargetLanguage();
            bool hasText = PostDetailsContentBuilder.HasVisibleText(m_contentHtml);
            bool hasTitle = !string.IsNullOrWhiteSpace(m_titleLabel?.Text);
            if (string.IsNullOrWhiteSpace(translateUrl)
                || string.IsNullOrWhiteSpace(targetLanguage)
                || string.IsNullOrWhiteSpace(m_contentHtml)
                || (!hasText && !hasTitle))
            {
                return;
            }

            SetTranslateStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostDetailsDialog", "TranslatingTo"), targetLanguage), new Color(160, 210, 255));
            // 服务端对相同内容有翻译缓存：先请求、返回后一次性建 Widget，不先建原文再重建。
            m_contentPanel.Children.Clear();
            int requestId = ++m_autoTranslateRequestId;
            string title = m_titleLabel?.Text;
            m_autoTranslateCancellation?.Cancel();
            m_autoTranslateCancellation?.Dispose();
            m_autoTranslateCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = m_autoTranslateCancellation.Token;
            m_autoTranslateTask = Task.Run(() => TranslateDialogAsync(requestId, translateUrl, targetLanguage, title, m_contentHtml, cancellationToken));
        }

        private void UpdateAutoTranslateTask()
        {
            if (m_autoTranslateTask == null || !m_autoTranslateTask.IsCompleted)
            {
                return;
            }

            AutoTranslateResult result;
            try
            {
                result = m_autoTranslateTask.Result;
            }
            catch (Exception ex)
            {
                result = new AutoTranslateResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
            finally
            {
                m_autoTranslateTask = null;
            }

            if (result == null || result.RequestId != m_autoTranslateRequestId)
            {
                return;
            }

            if (!result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostDetailsDialog", "AutoTranslateFailed"), result.Error));
                    SetTranslateStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostDetailsDialog", "AutomaticTranslationFailed"), result.Error), new Color(255, 170, 170));
                    BuildContent();
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.TranslatedTitle))
            {
                m_titleLabel.Text = result.TranslatedTitle;
            }

            SetTranslateStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostDetailsDialog", "AutomaticallyTranslatedTo"), result.TargetLanguage), new Color(170, 230, 170));
            BuildContent(result.TranslatedHtml);
        }

        private void SetTranslateStatus(string text, Color color)
        {
            m_translateStatusLabel.Text = text;
            m_translateStatusLabel.Color = color;
            m_translateStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(text);
        }

        private string GetTranslateUrl()
        {
            return Uri.TryCreate(m_baseUrl, UriKind.Absolute, out Uri baseUri) ? $"{baseUri.AbsoluteUri.TrimEnd('/')}/translate" : null;
        }

        private static async Task<AutoTranslateResult> TranslateDialogAsync(
            int requestId,
            string translateUrl,
            string targetLanguage,
            string title,
            string originalHtml,
            CancellationToken cancellationToken)
        {
            try
            {
                TranslateClient client = TranslateClient.Shared;
                // 标题与正文两请求相互独立，并行发出再逐项取结果（详情打开省一次串行往返）。
                Task<string> titleTask = string.IsNullOrWhiteSpace(title)
                    ? Task.FromResult(title)
                    : client.TranslateAsync(translateUrl, title, targetLanguage, cancellationToken);
                Task<string> htmlTask = client.TranslateAsync(translateUrl, originalHtml, targetLanguage, cancellationToken);
                await Task.WhenAll(titleTask, htmlTask);
                return new AutoTranslateResult
                {
                    RequestId = requestId,
                    Success = true,
                    TargetLanguage = targetLanguage,
                    TranslatedTitle = titleTask.Result,
                    TranslatedHtml = htmlTask.Result
                };
            }
            catch (Exception ex)
            {
                return new AutoTranslateResult
                {
                    RequestId = requestId,
                    Success = false,
                    Error = ex.Message,
                    TargetLanguage = targetLanguage
                };
            }
        }

        private static string GetSystemTargetLanguage()
        {
            CultureInfo culture = CultureInfo.CurrentUICulture == CultureInfo.InvariantCulture
                ? (CultureInfo.CurrentCulture == CultureInfo.InvariantCulture
                    ? CultureInfo.InstalledUICulture
                    : CultureInfo.CurrentCulture)
                : CultureInfo.CurrentUICulture;

            if (culture == CultureInfo.InvariantCulture)
            {
                return null;
            }
            CultureInfo languageCulture = culture.Parent != CultureInfo.InvariantCulture
                ? culture.Parent
                : culture;
            string englishName = languageCulture?.EnglishName;
            if (string.IsNullOrWhiteSpace(englishName))
            {
                return null;
            }

            int parenthesisIndex = englishName.IndexOf(" (", StringComparison.Ordinal);
            return parenthesisIndex > 0 ? englishName[..parenthesisIndex].Trim() : englishName.Trim();
        }

        private sealed class AutoTranslateResult
        {
            public int RequestId { get; set; }
            public bool Success { get; set; }
            public string TargetLanguage { get; set; }
            public string TranslatedTitle { get; set; }
            public string TranslatedHtml { get; set; }
            public string Error { get; set; }
        }
    }
}
