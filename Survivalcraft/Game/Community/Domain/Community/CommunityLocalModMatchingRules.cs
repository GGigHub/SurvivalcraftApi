using Engine;

namespace Game {
    static class CommunityLocalModMatchingRules {
        public static bool MatchesText(string left, string right) => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool MatchesFileName(string left, string right) {
            string normalizedLeft = NormalizeRemoteFileName(left);
            string normalizedRight = NormalizeRemoteFileName(right);
            return !string.IsNullOrWhiteSpace(normalizedLeft)
                && !string.IsNullOrWhiteSpace(normalizedRight)
                && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        // 社区可控文件名（Url/Filename）匹配前的归一化：去 URL 转义、统一分隔符、取末段。
        static string NormalizeRemoteFileName(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return null;
            }
            string text = value.Trim();
            try {
                text = Uri.UnescapeDataString(text);
            }
            catch (UriFormatException) {
                // 非法转义序列（如单独 "%"），回退原始裁剪值继续比较。
            }
            return Storage.GetFileName(text);
        }

        static bool MatchesLocalMod(PostVersionItem version, PostFileItem file, string packageName, string localFileName) => IsModFile(version, file)
            && ((!string.IsNullOrWhiteSpace(packageName) && MatchesText(file.PackageName, packageName))
                || (!string.IsNullOrWhiteSpace(localFileName) && MatchesFileName(file.Filename, localFileName)));

        /// <summary>存在性判定：命中即短路，不物化匹配列表。</summary>
        public static bool IsAnyMatchingFile(PostVersionItem version, string packageName, string localFileName) {
            return (version?.Files ?? []).Any(file => MatchesLocalMod(version, file, packageName, localFileName));
        }

        public static PostFileItem[] GetMatchingFiles(PostVersionItem version, string packageName, string localFileName) {
            return [.. (version?.Files ?? []).Where(file => MatchesLocalMod(version, file, packageName, localFileName))];
        }

        public static bool PostContainsLocalMod(PostItem post, string packageName, string localFileName) {
            return (post?.PostVersions ?? []).Where(version => version?.Files != null)
                .Any(version => IsAnyMatchingFile(version, packageName, localFileName));
        }

        /// <summary>
        /// mod 类文件判定（含 Unknown 类型按版本类型回退）。全项目唯一权威实现：
        /// ModUpdateSelector 等消费方一律复用，不得再造私有拷贝。
        /// </summary>
        public static bool IsModFile(PostVersionItem version, PostFileItem file) => file != null
            && (CommunityFileTypes.IsModLike(file.Type)
                || (file.Type == CommunityFileType.Unknown && CommunityFileTypes.IsModLike(version.FileType)));
    }
}