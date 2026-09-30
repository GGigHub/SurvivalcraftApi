using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Game
{
    /// <summary>
    /// 队列对话框的宿主抽象：提供队列条目数据与四项队列操作，由下载流程控制器实现。
    /// 对话框保持对宿主的无向依赖，便于替换与测试。
    /// </summary>
    public interface IDownloadQueueDialogHost
    {
        IReadOnlyList<CommunityDownloadQueueDialog.DownloadQueueDialogItem> GetQueueDialogItems();

        void PauseOrResumeDownloadQueueItem(int? itemId);

        void CancelDownloadQueueItem(int? itemId);

        void RetryDownloadQueueItem(int? itemId);

        void ClearFinishedDownloadQueueItems(int? itemId);
    }

    public sealed class CommunityDownloadQueueDialog : Dialog
    {
        private readonly IDownloadQueueDialogHost m_host;
        private readonly Action m_closedCallback;
        private readonly ListPanelWidget m_taskList;
        private readonly LabelWidget m_selectionLabel;
        private readonly ButtonWidget m_pauseResumeButton;
        private readonly ButtonWidget m_cancelButton;
        private readonly ButtonWidget m_retryButton;
        private readonly ButtonWidget m_clearButton;
        private readonly ButtonWidget m_closeButton;
        private readonly List<DownloadQueueDialogItem> m_rows = [];
        private int? m_selectedTaskId;

        public sealed class DownloadQueueDialogItem
        {
            public int Id { get; init; }

            public string Title { get; init; }

            public string Detail { get; init; }

            // 与宿主操作语义对齐的能力位（均为 State 的纯函数）：
            // 选中项时按钮按该项启用；无选中时按“存在任一可操作项”聚合启用（null id 作用于默认目标）。
            public bool CanPauseResume { get; init; }

            public bool CanCancel { get; init; }

            public bool CanRetry { get; init; }

            public bool CanClear { get; init; }
        }

        public CommunityDownloadQueueDialog(IDownloadQueueDialogHost host, Action closedCallback)
        {
            ArgumentNullException.ThrowIfNull(host);
            m_host = host;
            m_closedCallback = closedCallback;
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityDownloadQueueDialog");
            LoadContents(this, node);
            m_taskList = Children.Find<ListPanelWidget>("ScbbsPlusDownloadQueueDialog.TaskList");
            m_taskList.ItemWidgetFactory = MakeTaskItem;
            m_taskList.ItemClicked += OnTaskItemClicked;
            m_selectionLabel = Children.Find<LabelWidget>("ScbbsPlusDownloadQueueDialog.Selection");
            m_pauseResumeButton = Children.Find<ButtonWidget>("ScbbsPlusDownloadQueueDialog.PauseResumeButton");
            m_cancelButton = Children.Find<ButtonWidget>("ScbbsPlusDownloadQueueDialog.CancelButton");
            m_retryButton = Children.Find<ButtonWidget>("ScbbsPlusDownloadQueueDialog.RetryButton");
            m_clearButton = Children.Find<ButtonWidget>("ScbbsPlusDownloadQueueDialog.ClearButton");
            m_closeButton = Children.Find<ButtonWidget>("ScbbsPlusDownloadQueueDialog.CloseButton");
            Refresh();
        }

        // 下载进度/状态由后台线程持续更新且无变更通知事件，对话框打开期间需每帧轮询一次 Provider；
        // 按钮动作的处理结果也统一由 Update 末尾的这次 Refresh 反映，无需单独刷新。
        private void Refresh()
        {
            IReadOnlyList<DownloadQueueDialogItem> items = m_host.GetQueueDialogItems();
            if (NeedsRebuild(items))
            {
                RebuildRows(items);
            }
            else
            {
                UpdateRowItems(items);
            }

            if (m_selectedTaskId.HasValue && FindRowIndexById(m_selectedTaskId) == null)
            {
                m_selectedTaskId = null;
                m_taskList.SelectedIndex = null;
            }

            SyncListPanelSelection();
            RefreshRowVisuals();
            RefreshButtonsEnabled();
            UpdateSelectionLabel();
        }

        // ListPanelWidget 点击后无条件重选自身索引；本对话框在点击回调里按 m_selectedTaskId 对齐，
        // 由每帧 Refresh 末尾的 Sync 覆盖 ListPanel 的强制重选，取消选择当帧收敛。
        // Provider 每次返回新建的 DownloadQueueDialogItem 实例，故用 SelectedIndex 而非 SelectedItem 同步。
        private void SyncListPanelSelection()
        {
            m_taskList.SelectedIndex = FindRowIndexById(m_selectedTaskId);
        }

        private int? FindRowIndexById(int? taskId)
        {
            if (!taskId.HasValue)
            {
                return null;
            }
            for (int i = 0; i < m_rows.Count; i++)
            {
                if (m_rows[i].Id == taskId.Value)
                {
                    return i;
                }
            }
            return null;
        }

        private DownloadQueueDialogItem FindRowById(int? taskId)
        {
            return taskId.HasValue ? m_rows.FirstOrDefault(candidate => candidate.Id == taskId.Value) : null;
        }

        public override void Update()
        {
            if (m_pauseResumeButton.IsClicked)
            {
                m_host.PauseOrResumeDownloadQueueItem(m_selectedTaskId);
            }
            if (m_cancelButton.IsClicked)
            {
                m_host.CancelDownloadQueueItem(m_selectedTaskId);
            }
            if (m_retryButton.IsClicked)
            {
                m_host.RetryDownloadQueueItem(m_selectedTaskId);
            }
            if (m_clearButton.IsClicked)
            {
                m_host.ClearFinishedDownloadQueueItems(m_selectedTaskId);
            }
            if (m_closeButton.IsClicked
                || Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                m_closedCallback?.Invoke();
                DialogsManager.HideDialog(this);
                return;
            }
            Refresh();
        }

        private Widget MakeTaskItem(object item)
        {
            CommunityDownloadQueueItemWidget widget = new()
            {
                Title = (item as DownloadQueueDialogItem)?.Title ?? string.Empty,
                Detail = (item as DownloadQueueDialogItem)?.Detail ?? string.Empty
            };
            if (item is DownloadQueueDialogItem row)
            {
                widget.ApplySelection(m_selectedTaskId == row.Id);
            }
            return widget;
        }

        private void OnTaskItemClicked(object item)
        {
            if (item is not DownloadQueueDialogItem row)
            {
                return;
            }

            m_selectedTaskId = m_selectedTaskId == row.Id ? null : row.Id;
            RefreshRowVisuals();
            UpdateSelectionLabel();
        }

        private bool NeedsRebuild(IReadOnlyList<DownloadQueueDialogItem> items)
        {
            if (items.Count != m_rows.Count)
            {
                return true;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (m_rows[i].Id != items[i].Id)
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildRows(IReadOnlyList<DownloadQueueDialogItem> items)
        {
            m_rows.Clear();
            m_taskList.ClearItems();

            foreach (DownloadQueueDialogItem item in items)
            {
                m_rows.Add(item);
                m_taskList.AddItem(item);
            }
        }

        private void UpdateRowItems(IReadOnlyList<DownloadQueueDialogItem> items)
        {
            for (int i = 0; i < m_rows.Count; i++)
            {
                // Provider 缓存命中时返回同一实例，内容必然未变，整行跳过。
                if (ReferenceEquals(m_rows[i], items[i]))
                {
                    continue;
                }
                m_rows[i] = items[i];
                // ListPanel 的 ItemWidgetFactory 读 m_items[i]，滚动新入视图的行用它构建 widget；
                // 必须同步替换，否则缓存稳定后 ReferenceEquals 快路径会让滚入行永久显示旧文本。
                m_taskList.m_items[i] = items[i];
                if (m_taskList.m_widgetsByIndex.TryGetValue(i, out Widget widget)
                    && widget is CommunityDownloadQueueItemWidget itemWidget)
                {
                    itemWidget.Title = m_rows[i].Title ?? string.Empty;
                    itemWidget.Detail = m_rows[i].Detail ?? string.Empty;
                }
            }
        }

        private void RefreshRowVisuals()
        {
            foreach (KeyValuePair<int, Widget> pair in m_taskList.m_widgetsByIndex)
            {
                if (pair.Value is CommunityDownloadQueueItemWidget widget
                    && pair.Key < m_rows.Count)
                {
                    widget.ApplySelection(m_selectedTaskId == m_rows[pair.Key].Id);
                }
            }
        }

        // 按钮随选中项能力位启用/禁用；无选中时按“存在任一可操作项”聚合（对应 null id 作用于默认目标的语义）。
        // 禁用按钮不参与 Update（Widget.UpdateWidgetsHierarchy 跳过 !IsEnabled），点击自然无效。
        private void RefreshButtonsEnabled()
        {
            DownloadQueueDialogItem selected = FindRowById(m_selectedTaskId);
            bool canPauseResume;
            bool canCancel;
            bool canRetry;
            bool canClear;
            if (selected != null)
            {
                canPauseResume = selected.CanPauseResume;
                canCancel = selected.CanCancel;
                canRetry = selected.CanRetry;
                canClear = selected.CanClear;
            }
            else
            {
                canPauseResume = false;
                canCancel = false;
                canRetry = false;
                canClear = false;
                foreach (DownloadQueueDialogItem row in m_rows)
                {
                    canPauseResume |= row.CanPauseResume;
                    canCancel |= row.CanCancel;
                    canRetry |= row.CanRetry;
                    canClear |= row.CanClear;
                }
            }
            m_pauseResumeButton.IsEnabled = canPauseResume;
            m_cancelButton.IsEnabled = canCancel;
            m_retryButton.IsEnabled = canRetry;
            m_clearButton.IsEnabled = canClear;
        }

        private void UpdateSelectionLabel()
        {
            if (m_rows.Count == 0)
            {
                m_selectionLabel.Text = LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "NoDownloadTasks");
                return;
            }

            if (!m_selectedTaskId.HasValue)
            {
                m_selectionLabel.Text = LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "SelectionHint");
                return;
            }

            DownloadQueueDialogItem row = FindRowById(m_selectedTaskId);
            m_selectionLabel.Text = row == null
                ? LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "SelectionHint")
                : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "SelectedTask"), row.Id, ExtractDisplayName(row.Title));
        }

        private static string ExtractDisplayName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "-";
            }

            int statusEnd = title.IndexOf("] ", StringComparison.Ordinal);
            if (statusEnd >= 0 && statusEnd + 2 < title.Length)
            {
                return title[(statusEnd + 2)..].Trim();
            }

            int dotIndex = title.IndexOf(". ", StringComparison.Ordinal);
            return dotIndex >= 0 && dotIndex + 2 < title.Length ? title[(dotIndex + 2)..].Trim() : title.Trim();
        }
    }
}
