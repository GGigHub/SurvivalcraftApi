using System.Runtime.CompilerServices;
using Engine;

namespace Game {
    /// <summary>
    /// flex 主轴方向（物理轴）。bit0 = Vertical，bit1 = Reverse。
    /// XML 双写法等价：Direction="Vertical, Reverse" ≡ Direction="VerticalReverse"。
    /// </summary>
    [Flags]
    public enum FlexDirection {
        Horizontal = 0,
        Vertical = 1,
        NoReverse = 0,
        Reverse = 2,
        HorizontalReverse = 2,
        VerticalReverse = 3
    }

    /// <summary>
    /// flex 换行模式。bit0 = Wrap，bit1 = Reverse（行序反转），bit2 = Balance（均衡断行）。
    /// 均衡必换行（Balance 复合别名都含 bit0）；NoWrap 时 Reverse 位忽略（单行无行序概念）。
    /// </summary>
    [Flags]
    public enum FlexWrap {
        NoWrap = 0,
        NoReverse = 0,
        NoBalance = 0,
        Wrap = 1,
        Reverse = 2,
        Balance = 4,
        WrapReverse = 3,
        WrapBalance = 5,
        WrapReverseBalance = 7
    }

    /// <summary>flex 主轴分布模式（单维度互斥，不 Flags）。</summary>
    public enum FlexJustification {
        Start,
        Center,
        End,
        SpaceBetween,
        SpaceAround,
        SpaceEvenly
    }

    /// <summary>
    /// CSS flexbox 布局容器：Direction/Wrap/LineCount/Gap/JustifyContent/AlignItems 容器属性 +
    /// Grow/Shrink/Basis 子件 attached property（XML 写 FlexPanelWidget.Grow="1"，
    /// 本体 LoadProperties 原生 Type.Prop 语法）。
    /// 主轴期望恒有限（fill 子件基础 0，空间分配交给 grow）；交叉轴测量滤 Infinity/NaN 子件
    /// （同 AdaptiveCanvasWidget（屏批移植）特判）。AlignItems 用 Near 哨兵：子件交叉轴对齐为
    /// Near（enum 默认值）时用容器值替代，显式 Center/Far/Stretch 优先。
    /// 详见 docs/superpowers/specs/2026-08-24-flex-panel-widget-design.md。
    /// </summary>
    public class FlexPanelWidget : ContainerWidget {
        sealed class FlexItem {
            public float Grow;

            public float Shrink;

            public float Basis = -1f;
        }

        static readonly ConditionalWeakTable<Widget, FlexItem> m_items = new();

        public FlexDirection Direction { get; set; }

        public FlexWrap Wrap { get; set; }

        public int LineCount { get; set; } = 1;

        /// <summary>间隔（屏幕物理轴）：X 恒水平、Y 恒垂直。横排 X=子件间隔 Y=行距，竖排反之。</summary>
        public Vector2 Gap { get; set; }

        public FlexJustification JustifyContent { get; set; }

        /// <summary>交叉轴默认对齐（Near 哨兵）：子件交叉轴对齐为 Near 时以此替代。</summary>
        public WidgetAlignment AlignItems { get; set; }

        public static void SetGrow(Widget widget, float value) => GetItem(widget).Grow = value;

        public static float GetGrow(Widget widget) => GetItem(widget).Grow;

        public static void SetShrink(Widget widget, float value) => GetItem(widget).Shrink = value;

        public static float GetShrink(Widget widget) => GetItem(widget).Shrink;

        public static void SetBasis(Widget widget, float value) => GetItem(widget).Basis = value;

        public static float GetBasis(Widget widget) => GetItem(widget).Basis;

        static FlexItem GetItem(Widget widget) {
            if (!m_items.TryGetValue(widget, out FlexItem item)) {
                item = new FlexItem();
                m_items.Add(widget, item);
            }
            return item;
        }

        sealed class FlexRow {
            public readonly List<Widget> Items = [];

            /// <summary>行主轴基础宽（含各件 margin 与件间 gap，不含 grow/shrink 分配）。</summary>
            public float MainSize;

            /// <summary>行交叉轴高（含 margin）。</summary>
            public float CrossSize;
        }

        readonly List<FlexRow> m_rows = [];

        // 帧路径复用缓冲（每帧 Measure/Arrange 反复执行，避免逐帧分配）：
        readonly List<FlexRow> m_rowPool = [];
        readonly List<Widget> m_visibleBuffer = [];
        readonly Dictionary<Widget, float> m_adjustmentBuffer = [];

        FlexRow AcquireRow() {
            int last = m_rowPool.Count - 1;
            if (last >= 0) {
                FlexRow pooled = m_rowPool[last];
                m_rowPool.RemoveAt(last);
                pooled.Items.Clear();
                pooled.MainSize = 0f;
                pooled.CrossSize = 0f;
                return pooled;
            }
            return new FlexRow();
        }

        bool IsVertical => (Direction & FlexDirection.Vertical) != 0;

        bool IsMainReversed => (Direction & FlexDirection.Reverse) != 0;

        float MainGap => IsVertical ? Gap.Y : Gap.X;

        float CrossGap => IsVertical ? Gap.X : Gap.Y;

        float MarginMainSum(Widget w) => IsVertical ? w.MarginVerticalSum : w.MarginHorizontalSum;

        float MarginCrossSum(Widget w) => IsVertical ? w.MarginHorizontalSum : w.MarginVerticalSum;

        // 主轴基础尺寸：Basis >= 0 固定；否则子件主轴期望（Infinity/NaN 按 0，空间分配交给 grow）
        static float MainBasis(Widget child, bool isVertical) {
            float basis = GetBasis(child);
            if (basis >= 0f) {
                return basis;
            }
            float desired = isVertical ? child.ParentDesiredSize.Y : child.ParentDesiredSize.X;
            return IsFinite(desired) ? MathUtils.Max(desired, 0f) : 0f;
        }

        // 交叉轴贡献：Infinity/NaN 按 0（防污染，同 AdaptiveCanvasWidget（屏批移植）特判）
        static float CrossExtent(Widget child, bool isVertical) {
            float desired = isVertical ? child.ParentDesiredSize.X : child.ParentDesiredSize.Y;
            return IsFinite(desired) ? MathUtils.Max(desired, 0f) : 0f;
        }

        static bool IsFinite(float value) => !float.IsInfinity(value) && !float.IsNaN(value);

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            // 上一帧的行对象归还池复用（单线程 UI，帧序 Measure→Arrange 内 m_rows 仍有效）。
            m_rowPool.AddRange(m_rows);
            m_rows.Clear();
            foreach (Widget child in Children) {
                if (child.IsVisible) {
                    child.Measure(Vector2.Max(parentAvailableSize - child.MarginHorizontalSumAndVerticalSum, Vector2.Zero));
                }
            }
            // 断行：Balance 均衡 / Wrap 贪心（行宽超 available 换行）/ NoWrap 或 ∞ available 单行
            float availableMain = IsVertical ? parentAvailableSize.Y : parentAvailableSize.X;
            m_visibleBuffer.Clear();
            foreach (Widget child in Children) {
                if (child.IsVisible) {
                    m_visibleBuffer.Add(child);
                }
            }
            if ((Wrap & FlexWrap.Balance) != 0
                && IsFinite(availableMain)) {
                BreakBalanced(m_visibleBuffer, availableMain);
            }
            else {
                bool isWrapping = (Wrap & FlexWrap.Wrap) != 0 && IsFinite(availableMain);
                GreedyBreak(m_visibleBuffer, isWrapping ? availableMain : float.PositiveInfinity);
            }
            // 期望：主轴 = max 行宽；交叉轴 = Σ行高 + CrossGap×(行数-1)
            float main = 0f;
            float cross = 0f;
            foreach (FlexRow row in m_rows) {
                main = MathUtils.Max(main, row.MainSize);
                cross += row.CrossSize;
            }
            cross += CrossGap * MathUtils.Max(m_rows.Count - 1, 0);
            DesiredSize = IsVertical ? new Vector2(cross, main) : new Vector2(main, cross);
        }

        public override void ArrangeOverride() {
            // Reverse 反转交叉轴方向：首行从远端开始，后续行向近端堆叠。
            // 仅 Wrap 生效时成立——类文档约定 NoWrap（单行）下 Reverse 位忽略。
            float crossPos = 0f;
            if ((Wrap & FlexWrap.Wrap) != 0
                && (Wrap & FlexWrap.Reverse) != 0) {
                float totalCross = -CrossGap;
                foreach (FlexRow row in m_rows) {
                    totalCross += row.CrossSize + CrossGap;
                }
                crossPos = CrossAxisSize() - totalCross;
                for (int i = m_rows.Count - 1; i >= 0; i--) {
                    FlexRow row = m_rows[i];
                    ArrangeRow(row, crossPos);
                    crossPos += row.CrossSize + CrossGap;
                }
                return;
            }
            foreach (FlexRow row in m_rows) {
                ArrangeRow(row, crossPos);
                crossPos += row.CrossSize + CrossGap;
            }
        }

        float CrossAxisSize() => IsVertical ? ActualSize.X : ActualSize.Y;

        /// <summary>贪心断行填 m_rows：累加 basis+margin+MainGap 超 availableMain 换行（首件必进）。</summary>
        void GreedyBreak(List<Widget> items, float availableMain) {
            FlexRow current = AcquireRow();
            foreach (Widget child in items) {
                float basis = MainBasis(child, IsVertical) + MarginMainSum(child);
                float projected = current.Items.Count == 0 ? basis : current.MainSize + MainGap + basis;
                if (current.Items.Count > 0
                    && projected > availableMain) {
                    m_rows.Add(current);
                    current = AcquireRow();
                    projected = basis;
                }
                current.Items.Add(child);
                current.MainSize = projected;
                current.CrossSize = MathUtils.Max(current.CrossSize, CrossExtent(child, IsVertical) + MarginCrossSum(child));
            }
            if (current.Items.Count > 0) {
                m_rows.Add(current);
            }
        }

        /// <summary>按行宽上限 W 贪心断行的行数（二分探测用，不构造行）。</summary>
        int CountLines(List<Widget> items, float limit) {
            int lines = 0;
            float rowMain = 0f;
            bool hasItem = false;
            foreach (Widget child in items) {
                float basis = MainBasis(child, IsVertical) + MarginMainSum(child);
                float projected = hasItem ? rowMain + MainGap + basis : basis;
                if (hasItem && projected > limit) {
                    lines++;
                    hasItem = false;
                    projected = basis;
                }
                hasItem = true;
                rowMain = projected;
            }
            return hasItem ? lines + 1 : lines;
        }

        /// <summary>
        /// 均衡断行（保序）：N = max(贪心行数, LineCount)，二分最小化最大行宽；
        /// 行数不足拆最宽行补足，件数不足补空行（高 0）。
        /// </summary>
        void BreakBalanced(List<Widget> items, float availableMain) {
            int lineTarget = MathUtils.Max(CountLines(items, availableMain), LineCount);
            float lo = 0f;
            float hi = 0f;
            foreach (Widget child in items) {
                float basis = MainBasis(child, IsVertical) + MarginMainSum(child);
                lo = MathUtils.Max(lo, basis);
                hi += basis;
            }
            hi += MainGap * MathUtils.Max(items.Count - 1, 0);
            hi = MathUtils.Max(hi, lo);
            // 二分 min{W : CountLines(W) <= lineTarget}；不变式 CountLines(hi)=1 行 <= lineTarget
            while (hi - lo > 0.01f) {
                float mid = (lo + hi) / 2f;
                if (CountLines(items, mid) <= lineTarget) {
                    hi = mid;
                }
                else {
                    lo = mid;
                }
            }
            GreedyBreak(items, hi);
            while (m_rows.Count < lineTarget) {
                FlexRow widest = null;
                foreach (FlexRow row in m_rows) {
                    if (row.Items.Count >= 2
                        && (widest == null || row.MainSize > widest.MainSize)) {
                        widest = row;
                    }
                }
                if (widest == null) {
                    m_rows.Add(AcquireRow());
                    continue;
                }
                FlexRow split = AcquireRow();
                split.Items.Add(widest.Items[^1]);
                widest.Items.RemoveAt(widest.Items.Count - 1);
                RecalculateRow(widest);
                RecalculateRow(split);
                m_rows.Insert(m_rows.IndexOf(widest) + 1, split);
            }
        }

        void RecalculateRow(FlexRow row) {
            row.MainSize = 0f;
            row.CrossSize = 0f;
            for (int i = 0; i < row.Items.Count; i++) {
                Widget child = row.Items[i];
                if (i > 0) {
                    row.MainSize += MainGap;
                }
                row.MainSize += MainBasis(child, IsVertical) + MarginMainSum(child);
                row.CrossSize = MathUtils.Max(row.CrossSize, CrossExtent(child, IsVertical) + MarginCrossSum(child));
            }
        }

        float MainAxisSize() => IsVertical ? ActualSize.Y : ActualSize.X;

        void ArrangeRow(FlexRow row, float crossStart) {
            float mainTotal = MainAxisSize();
            // grow/shrink 分配：剩余按 grow/Σgrow 加、溢出按 shrink×基础 加权扣（下限 0）。
            // 分配表用成员缓冲复用（每帧每行一次，逐行 Clear）。
            float freeRaw = mainTotal - row.MainSize;
            m_adjustmentBuffer.Clear();
            float freeSpace = 0f;
            if (freeRaw > 0f) {
                float totalGrow = 0f;
                foreach (Widget child in row.Items) {
                    totalGrow += MathUtils.Max(GetGrow(child), 0f);
                }
                if (totalGrow > 0f) {
                    foreach (Widget child in row.Items) {
                        float grow = GetGrow(child);
                        if (grow > 0f) {
                            m_adjustmentBuffer[child] = freeRaw * grow / totalGrow;
                        }
                    }
                }
                else {
                    freeSpace = freeRaw;
                }
            }
            else if (freeRaw < 0f) {
                float totalWeightedShrink = 0f;
                foreach (Widget child in row.Items) {
                    float shrink = GetShrink(child);
                    if (shrink > 0f) {
                        totalWeightedShrink += shrink * MainBasis(child, IsVertical);
                    }
                }
                if (totalWeightedShrink > 0f) {
                    foreach (Widget child in row.Items) {
                        float shrink = GetShrink(child);
                        float basis = MainBasis(child, IsVertical);
                        if (shrink > 0f
                            && basis > 0f) {
                            m_adjustmentBuffer[child] = -MathUtils.Min(-freeRaw * shrink * basis / totalWeightedShrink, basis);
                        }
                    }
                }
            }
            // 主轴分布：剩余（grow 未吃部分）按 JustifyContent 摆
            int count = row.Items.Count;
            float start;
            float between;
            switch (JustifyContent) {
                case FlexJustification.Center:
                    start = freeSpace / 2f;
                    between = 0f;
                    break;
                case FlexJustification.End:
                    start = freeSpace;
                    between = 0f;
                    break;
                case FlexJustification.SpaceBetween:
                    start = 0f;
                    between = count > 1 ? freeSpace / (count - 1) : 0f;
                    break;
                case FlexJustification.SpaceAround:
                    start = freeSpace / count / 2f;
                    between = freeSpace / count;
                    break;
                case FlexJustification.SpaceEvenly:
                    start = freeSpace / (count + 1);
                    between = freeSpace / (count + 1);
                    break;
                default:
                    start = 0f;
                    between = 0f;
                    break;
            }
            float mainPos = start;
            foreach (Widget child in row.Items) {
                float allocated = MainBasis(child, IsVertical) + MarginMainSum(child);
                bool growFills = false;
                if (m_adjustmentBuffer.TryGetValue(child, out float adjustment)) {
                    allocated += adjustment;
                    // grow 分配的是 used size：子件无条件吃满分配区间（CSS flex-grow 语义），
                    // shrink（负 adjustment）只收分配、按 min(desired, alloc) 摆
                    growFills = adjustment > 0f;
                }
                ArrangeChildInFlexCell(child, mainPos, allocated, crossStart, row.CrossSize, growFills);
                mainPos += MathUtils.Max(allocated, 0f) + MainGap + between;
            }
        }

        /// <summary>
        /// cell 内摆位（ArrangeChildWidgetInCell 语义按主/交叉轴展开 + AlignItems Near 哨兵注入），
        /// 不改子件 Alignment 属性避免污染。主轴对齐用子件自身：Near 按基础摆、Stretch 占满分配区间；
        /// 交叉轴子件 Near 时用容器 AlignItems。Direction Reverse 先镜像 cell，再沿物理轴应用 margin。
        /// </summary>
        void ArrangeChildInFlexCell(Widget child, float mainPos, float allocMain, float crossStart, float crossSize, bool growFills = false) {
            WidgetAlignment mainAlign = IsVertical ? child.VerticalAlignment : child.HorizontalAlignment;
            WidgetAlignment crossAlign = IsVertical ? child.HorizontalAlignment : child.VerticalAlignment;
            if (crossAlign == WidgetAlignment.Near) {
                crossAlign = AlignItems;
            }
            float desiredMain = MainBasis(child, IsVertical);
            float desiredCross = CrossExtent(child, IsVertical);
            float mainMarginStart = IsVertical ? child.MarginTop : child.MarginLeft;
            float mainMarginEnd = IsVertical ? child.MarginBottom : child.MarginRight;
            float crossMarginStart = IsVertical ? child.MarginLeft : child.MarginTop;
            float crossMarginEnd = IsVertical ? child.MarginRight : child.MarginBottom;
            float cellMainStart = mainPos;
            float cellMainEnd = mainPos + allocMain;
            if (IsMainReversed) {
                cellMainStart = MainAxisSize() - (mainPos + allocMain);
                cellMainEnd = MainAxisSize() - mainPos;
            }
            // 主轴期望 ∞ 的 fill 件：basis 0 但语义是拉伸吃满分配区间（不论对齐）；
            // grow 吃到分配的子件同样无条件吃满（CSS flex-grow 拉伸 used size）
            bool wantsMainFill = growFills || (GetBasis(child) < 0f && !IsFinite(IsVertical ? child.ParentDesiredSize.Y : child.ParentDesiredSize.X));
            float sizeMain = wantsMainFill
                ? MathUtils.Max(allocMain - mainMarginStart - mainMarginEnd, 0f)
                : MathUtils.Min(desiredMain, MathUtils.Max(allocMain - mainMarginStart - mainMarginEnd, 0f));
            float posMain;
            if (mainAlign == WidgetAlignment.Stretch) {
                posMain = cellMainStart + mainMarginStart;
                sizeMain = MathUtils.Max(allocMain - mainMarginStart - mainMarginEnd, 0f);
            }
            else if (mainAlign == WidgetAlignment.Center) {
                posMain = cellMainStart + MathUtils.Max((allocMain - sizeMain) / 2f, 0f);
            }
            else if (mainAlign == WidgetAlignment.Far) {
                posMain = cellMainEnd - sizeMain - mainMarginEnd;
            }
            else {
                posMain = cellMainStart + mainMarginStart;
            }
            float sizeCross = MathUtils.Min(desiredCross, MathUtils.Max(crossSize - crossMarginStart - crossMarginEnd, 0f));
            float posCross;
            if (crossAlign == WidgetAlignment.Stretch) {
                posCross = crossStart + crossMarginStart;
                sizeCross = MathUtils.Max(crossSize - crossMarginStart - crossMarginEnd, 0f);
            }
            else if (crossAlign == WidgetAlignment.Center) {
                posCross = crossStart + MathUtils.Max((crossSize - sizeCross) / 2f, 0f);
            }
            else if (crossAlign == WidgetAlignment.Far) {
                posCross = crossStart + MathUtils.Max(crossSize - sizeCross - crossMarginEnd, 0f);
            }
            else {
                posCross = crossStart + crossMarginStart;
            }
            Vector2 pos = IsVertical ? new Vector2(posCross, posMain) : new Vector2(posMain, posCross);
            Vector2 size = IsVertical ? new Vector2(sizeCross, sizeMain) : new Vector2(sizeMain, sizeCross);
            child.Arrange(pos, size);
        }
    }
}