using Engine;

namespace Game.Network;

/// <summary>
///     服务端维护的“会同步给客户端”的生物位置历史，用于弹射物命中回溯补偿。
///     客户端把生物渲染在 0.6 秒前的服务端时间线上（NetworkBodyInterpolation.RemoteBodyDelay），
///     因此远程玩家发射的弹射物也需要把目标身体回退到相同时间后再做射线检测，
///     才能命中玩家屏幕上看到的目标。
/// </summary>
public static class NetworkBodyHistory {
    private const int MaxEntriesPerBody = 256;

    private const double HistoryDuration = 2.0;

    private readonly struct Entry(double time, Vector3 position) {
        public readonly double Time = time;
        public readonly Vector3 Position = position;
    }

    private sealed class BodyHistory {
        public readonly List<Entry> Entries = [];
    }

    private static readonly Dictionary<ComponentBody, BodyHistory> s_histories = [];

    /// <summary>
    ///     是否需要记录位置历史。只有会以 0.6s 延迟插值同步给客户端的身体需要记录，
    ///     即所有生物（动物、玩家）。房主本人对其他客户端来说同样是延迟插值渲染的。
    /// </summary>
    public static bool ShouldRecord(ComponentBody body) {
        return body?.Entity?.FindComponent<ComponentCreature>() != null;
    }

    public static void Record(ComponentBody body) {
        if (body == null) {
            return;
        }
        if (!s_histories.TryGetValue(body, out BodyHistory history)) {
            history = new BodyHistory();
            s_histories[body] = history;
        }
        List<Entry> entries = history.Entries;
        double time = Engine.Time.RealTime;
        if (entries.Count > 0) {
            Entry last = entries[^1];
            if (time < last.Time) {
                return;
            }
            if (time == last.Time) {
                entries[^1] = new Entry(time, body.Position);
                return;
            }
        }
        entries.Add(new Entry(time, body.Position));
        if (entries.Count > MaxEntriesPerBody) {
            entries.RemoveAt(0);
        }
        double minTime = time - HistoryDuration;
        int removeCount = 0;
        while (removeCount < entries.Count && entries[removeCount].Time < minTime) {
            removeCount++;
        }
        if (removeCount > 0) {
            entries.RemoveRange(0, removeCount);
        }
    }

    /// <summary>
    ///     获取身体在指定时间的包围盒。历史不足（例如身体刚创建）时返回 false，
    ///     由调用方回退到身体当前包围盒。
    /// </summary>
    public static bool TryGetBoxAt(ComponentBody body, double time, out BoundingBox box) {
        box = default;
        if (body == null || !s_histories.TryGetValue(body, out BodyHistory history)) {
            return false;
        }
        List<Entry> entries = history.Entries;
        if (entries.Count == 0) {
            return false;
        }
        Entry first = entries[0];
        Entry last = entries[^1];
        if (time <= first.Time) {
            box = CreateBox(body, first.Position);
            return true;
        }
        if (time >= last.Time) {
            box = CreateBox(body, last.Position);
            return true;
        }
        int low = 0;
        int high = entries.Count - 1;
        while (high - low > 1) {
            int middle = (low + high) / 2;
            if (entries[middle].Time <= time) {
                low = middle;
            }
            else {
                high = middle;
            }
        }
        Entry a = entries[low];
        Entry b = entries[high];
        float factor = (float)((time - a.Time) / (b.Time - a.Time));
        box = CreateBox(body, Vector3.Lerp(a.Position, b.Position, factor));
        return true;
    }

    /// <summary>
    ///     客户端实际渲染延迟并不是固定值：身体快照按 DefaultSnapshotInterval 间隔到达，
    ///     客户端播放时钟在追赶与等待之间摆动，实际延迟大致落在
    ///     [RemoteBodyDelay - ε, RemoteBodyDelay + DefaultSnapshotInterval + ε] 区间内。
    ///     只回溯单个时间点无法稳定覆盖客户端屏幕上目标的位置，因此在整个延迟窗口内
    ///     采样并合并包围盒，让命中判定覆盖目标在客户端可能出现的位置范围。
    /// </summary>
    public const double RewindWindowStart = NetworkBodyInterpolation.RemoteBodyDelay - 0.05;

    public const double RewindWindowEnd = NetworkBodyInterpolation.RemoteBodyDelay + NetworkBodyInterpolation.DefaultSnapshotInterval + 0.05;

    public const int RewindWindowSampleCount = 4;

    /// <summary>
    ///     获取身体在回溯补偿窗口内的合并包围盒。
    ///     <paramref name="excludedBody"/>（弹射物发射者本人的身体）在发射者客户端是实时渲染的，不参与补偿。
    ///     没有历史数据（例如非生物身体）时返回 false，由调用方回退到身体当前包围盒。
    /// </summary>
    public static bool TryGetCompensatedBoxAt(ComponentBody body, double now, ComponentBody excludedBody, out BoundingBox box) {
        box = default;
        if (body == null || body == excludedBody || !s_histories.ContainsKey(body)) {
            return false;
        }
        bool any = false;
        for (int i = 0; i < RewindWindowSampleCount; i++) {
            double rewindTime = now - MathUtils.Lerp(RewindWindowStart, RewindWindowEnd, (float)i / (RewindWindowSampleCount - 1));
            if (TryGetBoxAt(body, rewindTime, out BoundingBox sample)) {
                if (!any) {
                    box = sample;
                    any = true;
                }
                else {
                    box.Min = Vector3.Min(box.Min, sample.Min);
                    box.Max = Vector3.Max(box.Max, sample.Max);
                }
            }
        }
        return any;
    }

    public static void RemoveTarget(ComponentBody body) {
        if (body != null) {
            s_histories.Remove(body);
        }
    }

    public static void Clear() {
        s_histories.Clear();
    }

    private static BoundingBox CreateBox(ComponentBody body, Vector3 position) {
        Vector3 stanceBoxSize = body.StanceBoxSize;
        return new BoundingBox(
            position - new Vector3(stanceBoxSize.X / 2f, 0f, stanceBoxSize.Z / 2f),
            position + new Vector3(stanceBoxSize.X / 2f, stanceBoxSize.Y, stanceBoxSize.Z / 2f)
        );
    }
}
