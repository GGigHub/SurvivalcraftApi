using NuGet.Versioning;

namespace Game
{
    /// <summary>一次更新检查中「本地版本 vs 远端最新版本」的可判定比较结论。</summary>
    internal enum UpdateComparisonState
    {
        /// <summary>远端最新版本比本地新，应提示更新。</summary>
        UpdateAvailable,
        /// <summary>两侧版本一致，无更新。</summary>
        UpToDate,
        /// <summary>本地版本比远端最新还新（典型于开发版先于发布版）。</summary>
        CurrentVersionNewer,
        /// <summary>任一侧版本号无法解析，无法比较——不得报「已最新」。</summary>
        Incomparable
    }

    internal static class UpdateComparisonEvaluator
    {
        /// <summary>
        /// 以服务侧计算的 HasUpdate 为锚点，把「无更新」细分为三态：远端版本解析失败
        /// 归为 Incomparable 而非 UpToDate（旧逻辑会谎报已最新），本地更新（开发版）单列。
        /// </summary>
        public static UpdateComparisonState Evaluate(bool hasUpdate, NuGetVersion current, NuGetVersion latest)
        {
            if (hasUpdate)
            {
                return UpdateComparisonState.UpdateAvailable;
            }
            if (current == null || latest == null)
            {
                return UpdateComparisonState.Incomparable;
            }

            int compare = VersionComparer.VersionRelease.Compare(latest, current);
            if (compare > 0)
            {
                // HasUpdate=false 却 latest>current：两侧版本解析来源不一致，按有更新处理。
                return UpdateComparisonState.UpdateAvailable;
            }
            return compare < 0 ? UpdateComparisonState.CurrentVersionNewer : UpdateComparisonState.UpToDate;
        }
    }
}
