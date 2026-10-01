using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal interface IModpackSourceResolver
    {
        Task<PostFileItem> FetchFileInfoByIdAsync(
            string apiBaseUrl,
            string token,
            string fileId,
            CancellationToken cancellationToken = default);

        Task<(byte[] data, string downloadedFileName, string resolvedUrl, PostFileItem communityFile)> ResolveAsync(
            ModpackItem item,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default);
    }
}
