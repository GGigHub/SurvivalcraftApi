using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;

namespace Game
{
    internal sealed class CommunityPlayedTimeStore
    {
        private const int CurrentVersion = 1;
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public int Version { get; set; } = CurrentVersion;
        public Dictionary<string, ModPlayedTimeData> Mods { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static CommunityPlayedTimeStore Load(string path = null)
        {
            path ??= CommunityStoragePaths.PlayedTimeFile;
            if (!Storage.FileExists(path))
            {
                return CreateEmpty();
            }

            CommunityPlayedTimeStore store = JsonFileStore.Read<CommunityPlayedTimeStore>(path, s_jsonOptions);
            store ??= CreateEmpty();
            store.Mods ??= new Dictionary<string, ModPlayedTimeData>(StringComparer.OrdinalIgnoreCase);
            return store;
        }

        public void Save(string path = null)
        {
            path ??= CommunityStoragePaths.PlayedTimeFile;
            Version = CurrentVersion;
            Mods ??= new Dictionary<string, ModPlayedTimeData>(StringComparer.OrdinalIgnoreCase);
            JsonFileStore.Write(path, this, s_jsonOptions);
        }

        public static CommunityPlayedTimeStore CreateEmpty()
        {
            return new CommunityPlayedTimeStore();
        }
    }

    internal sealed class ModPlayedTimeData
    {
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public double TotalSeconds { get; set; }
        public DateTime LastStartedAtUtc { get; set; }
        public DateTime LastPlayedAtUtc { get; set; }
        public Dictionary<string, double> WorldSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
