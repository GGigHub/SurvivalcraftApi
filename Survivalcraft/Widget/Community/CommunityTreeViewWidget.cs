using System;
using System.Globalization;
using Engine;

namespace Game
{
    /// <summary>
    /// 引擎 TreeViewWidget 的 mod 侧替代：补上行组件重建的可见性。
    /// 引擎实现在窗口尺寸变化、展开/收起、Clear/AddRoot、图标更新等时机于 Draw 中
    /// 整体销毁重建全部行组件（UpdateNodes），且不提供任何事件；消费方刷在行组件上的
    /// 定制（图标尺寸、评分星、附加文本）随之静默回落模板默认值。
    /// 本类 override 引擎的虚方法 UpdateNodes，在重建完成后同步触发
    /// <see cref="RowWidgetsRebuilt"/>，此时新行已挂树且 node.LinkedWidget 已指向新行。
    /// 所有权约定：TreeViewNode.Icon/Subtexture 是弱引用（指向 PostIconLoader
    /// 的进程级缓存或引擎内容资产），任何路径都不得让节点走到 Dispose——否则共享纹理
    /// 会被节点销毁。清子树用 ClearChildren(dispose: false)，清树用 Clear(false)。
    /// </summary>
    public class CommunityTreeViewWidget : TreeViewWidget
    {
        /// <summary>行组件重建完成后同步触发（Draw 阶段、主线程）。</summary>
        public event Action RowWidgetsRebuilt;

        /// <summary>
        /// 引擎的 m_selectedNode 在 Clear/UpdateNodes 时不重置：列表刷新后仍指向旧列表节点，
        /// 消费方据 Tag 下载/查看会作用于不可见旧节点。仅当选中节点仍可达于 Nodes 树时返回，
        /// 否则视为未选中（null）。调用方须以本类型静态引用本属性。
        /// </summary>
        public new TreeViewNode SelectedNode
        {
            get
            {
                TreeViewNode selected = base.SelectedNode;
                if (selected == null)
                {
                    return null;
                }
                foreach (TreeViewNode root in Nodes)
                {
                    if (ContainsNode(root, selected))
                    {
                        return selected;
                    }
                }
                return null;
            }
        }

        private static bool ContainsNode(TreeViewNode node, TreeViewNode target)
        {
            if (node == target)
            {
                return true;
            }
            foreach (TreeViewNode child in node.Nodes)
            {
                if (ContainsNode(child, target))
                {
                    return true;
                }
            }
            return false;
        }

        public override void UpdateNodes()
        {
            base.UpdateNodes();
            try
            {
                RowWidgetsRebuilt?.Invoke();
            }
            catch (Exception e)
            {
                // 事件在引擎 Draw 主循环内同步触发，异常会打断整帧绘制；记录并放弃本帧重刷。
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTreeViewWidget", "RowWidgetsRebuiltCallbackFaulted"), e.Message));
            }
        }

        /// <summary>强制重建行组件（转调引擎脏标记；节点模型的增删改不会自动重建，需调用本方法）。</summary>
        public void InvalidateRows()
        {
            m_widgetsDirty = true;
        }
    }
}
