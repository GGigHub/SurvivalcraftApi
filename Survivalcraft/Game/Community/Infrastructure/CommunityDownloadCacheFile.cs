using System;
using System.Text.Json;
using Engine;

namespace Game
{
    internal static class CommunityDownloadCacheFile
    {
        internal sealed class Metadata
        {
            public string RequestUrl { get; set; }
            public string ResponseUrl { get; set; }
            public string ETag { get; set; }
            public DateTimeOffset? LastModified { get; set; }
            public long? TotalLength { get; set; }
        }

        public static bool TryDelete(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            try
            {
                Storage.DeleteFileIfExists(path);
                Storage.DeleteFileIfExists(GetMetadataPath(path));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryReadMetadata(string path, out Metadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string metadataPath = GetMetadataPath(path);
                if (!Storage.FileExists(metadataPath))
                {
                    return false;
                }

                metadata = JsonSerializer.Deserialize<Metadata>(Storage.ReadAllText(metadataPath));
                return metadata != null && !string.IsNullOrWhiteSpace(metadata.RequestUrl);
            }
            catch
            {
                metadata = null;
                return false;
            }
        }

        public static void WriteMetadata(string path, Metadata metadata)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A cache path is required.", nameof(path));
            }
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            JsonFileStore.Write(GetMetadataPath(path), metadata);
        }

        private static string GetMetadataPath(string path)
        {
            return path + ".meta.json";
        }
    }
}
