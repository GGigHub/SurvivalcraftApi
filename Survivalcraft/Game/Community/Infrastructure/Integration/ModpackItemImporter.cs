using System;
using System.Globalization;
using System.IO;

namespace Game
{
    /// <summary>
    /// 导入单个整合包项目，集中保留模组元数据校验和游戏内容写入。
    /// </summary>
    internal sealed class ModpackItemImporter : IModpackItemImporter
    {
        private readonly string m_selfPackageName;
        private readonly Func<IRemoteImportHost> m_hostProvider;

        public ModpackItemImporter(
            string selfPackageName,
            Func<IRemoteImportHost> hostProvider)
        {
            m_selfPackageName = selfPackageName ?? throw new ArgumentNullException(nameof(selfPackageName));
            m_hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
        }

        public void ImportExternalContent(
            int requestId,
            string itemUrl,
            string fileName,
            ExternalContentType type,
            byte[] data,
            ModpackItem item)
        {
            if (CommunityFileTypes.IsModLike(type)
                && (!string.IsNullOrWhiteSpace(item?.PackageName) || !string.IsNullOrWhiteSpace(item?.Version)))
            {
                ValidateModItem(data, item);
            }

            RemoteImportRequest request = new()
            {
                Url = itemUrl,
                FileName = fileName,
                Type = type
            };
            using MemoryStream analysisStream = new(data, writable: false);
            ExternalContentPreparation preparation = ExternalContentManager.PrepareExternalContent(m_selfPackageName, requestId, request, analysisStream);
            using MemoryStream importStream = new(data, writable: false);
            ExternalContentManager.ImportPreparedExternalContent(m_selfPackageName, importStream, preparation, overwriteExisting: true);
        }

        public bool ImportDataFile(byte[] data, ModpackItem item)
        {
            // host 缺失（离屏被释放/单槽被清）时数据无处导入：返回 false 让管线
            // 按失败计数。原先静默 no-op 后管线照常计成功、清单照常保存，
            // 整合包实际缺文件而 toast 报成功。
            IRemoteImportHost host = m_hostProvider();
            if (host == null)
            {
                return false;
            }
            host.ImportModpackDataFileSafe(data, item);
            // 导入后复检落盘结果：host 非空但为空操作实现（基类默认——社区/More 屏
            // 不处理数据文件）时，文件未落地在此暴露，同样按失败处理（复审 F3）。
            return host.IsModpackDataFilePresent(item);
        }

        internal static void ValidateModItem(byte[] scmodData, ModpackItem item)
        {
            // JSON 损坏时 TryReadModInfo 返回 true 但 modInfo 为 null：与条目缺失同按无效包跳过。
            if (!ScmodArchiveReader.TryReadModInfo(new MemoryStream(scmodData, writable: false), out ModInfo modInfo)
                || modInfo == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackItemImporter", "ModinfoMissingSkipped"), item.Filename));
            }

            if (!string.IsNullOrWhiteSpace(item.PackageName)
                && !string.Equals(modInfo.PackageName, item.PackageName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackItemImporter", "PackageNameMismatchSkipped"),
                    item.Filename,
                    item.PackageName,
                    modInfo.PackageName ?? LanguageControl.None));
            }
            if (!string.IsNullOrWhiteSpace(item.Version)
                && !string.Equals(modInfo.Version, item.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackItemImporter", "VersionMismatchSkipped"),
                    item.Filename,
                    item.Version,
                    modInfo.Version ?? LanguageControl.None));
            }
        }
    }
}
