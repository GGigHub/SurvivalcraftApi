using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    // 缺失依赖查询：按包名并发查社区资源目录（限流 4），聚齐可下载文件与缺失清单。
    internal sealed partial class LocalModNetworkManager
    {
        /// <summary>并发查询各依赖包名：命中文件回填类型/包名缺省后收集，查询失败或无文件按缺失处理。</summary>
        internal static async Task<ModDependencyLookupData> LookupModDependenciesAsync(
            ModCatalogClient catalogClient,
            string apiBaseUrl,
            string token,
            IReadOnlyList<ModDependencyInfo> dependencies,
            CancellationToken cancellationToken = default)
        {
            ModDependencyLookupData result = new();
            List<ModDependencyInfo> validDependencies = [];
            foreach (ModDependencyInfo dependency in dependencies ?? [])
            {
                string packageName = dependency?.PackageName?.Trim();
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                AddPreferredVersion(result.PreferredVersionsByPackage, packageName, dependency.VersionRangeString);
                validDependencies.Add(dependency);
            }

            // 并发查询依赖并限流 4，避免 N 依赖 N 次顺序往返；WhenAll 保持原顺序，聚合仍在主线程无需加锁。
            // 单项查询失败只影响该项（按未找到处理），不拖垮其余依赖的结果。
            using SemaphoreSlim limiter = new(4);
            Task<(ModDependencyInfo Dependency, UploadModsResponse Response, string Error)>[] tasks = validDependencies
                .Select(async dependency =>
                {
                    await limiter.WaitAsync(cancellationToken);
                    try
                    {
                        UploadModsResponse response = await catalogClient.FindByPackageNameAsync(
                            apiBaseUrl,
                            token,
                            dependency.PackageName.Trim(),
                            dependency.VersionRangeString,
                            cancellationToken: cancellationToken);
                        return (dependency, response, (string)null);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // 用户取消：不吞，整体取消传播。
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return (dependency, null, ex.Message);
                    }
                    finally
                    {
                        limiter.Release();
                    }
                })
                .ToArray();

            foreach ((ModDependencyInfo dependency, UploadModsResponse response, string error) in await Task.WhenAll(tasks))
            {
                if (error != null)
                {
                    // 查询失败按缺失处理：与查无结果同语义（调用方走缺失提示，用户可重试），
                    // 但不再让单个失败丢弃其余依赖的查询结果。
                    result.MissingDependencies.Add(dependency);
                    continue;
                }

                string packageName = dependency.PackageName.Trim();
                bool hasMatchedFile = false;
                if (response?.Code == 200 && response.Data?.List != null)
                {
                    foreach (PostFileItem file in response.Data.List)
                    {
                        if (file == null || string.IsNullOrWhiteSpace(file.Url))
                        {
                            continue;
                        }

                        file.Type = file.Type == CommunityFileType.Unknown ? CommunityFileType.Mod : file.Type;
                        file.PackageName = string.IsNullOrWhiteSpace(file.PackageName) ? packageName : file.PackageName;
                        file.PackageVersion = string.IsNullOrWhiteSpace(file.PackageVersion) ? dependency.VersionRangeString : file.PackageVersion;
                        result.Files.Add(file);
                        hasMatchedFile = true;
                    }
                }

                if (!hasMatchedFile)
                {
                    result.MissingDependencies.Add(dependency);
                }
            }
            return result;
        }

        private static void AddPreferredVersion(
            IDictionary<string, string> versionsByPackage,
            string packageName,
            string version)
        {
            string normalizedVersion = version?.Trim();
            if (!versionsByPackage.TryGetValue(packageName, out string existingVersion)
                || string.IsNullOrWhiteSpace(existingVersion))
            {
                versionsByPackage[packageName] = normalizedVersion;
            }
        }
    }

    internal sealed class ModDependencyLookupData
    {
        public List<PostFileItem> Files { get; } = [];
        public List<ModDependencyInfo> MissingDependencies { get; } = [];
        public Dictionary<string, string> PreferredVersionsByPackage { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
