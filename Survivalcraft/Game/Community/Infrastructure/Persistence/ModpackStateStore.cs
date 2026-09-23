using System.Text.Json;
using Engine;

namespace Game
{
    internal static class ModpackStateStore
    {
        private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

        public static LocalModpackActivationState Load()
        {
            return Storage.FileExists(CommunityStoragePaths.LocalModpackStateFile)
                ? JsonFileStore.Read<LocalModpackActivationState>(
                    CommunityStoragePaths.LocalModpackStateFile,
                    s_options)
                : null;
        }

        public static void Save(LocalModpackActivationState state)
        {
            if (state == null)
            {
                if (Storage.FileExists(CommunityStoragePaths.LocalModpackStateFile))
                {
                    Storage.DeleteFile(CommunityStoragePaths.LocalModpackStateFile);
                }
                return;
            }

            JsonFileStore.Write(CommunityStoragePaths.LocalModpackStateFile, state, s_options);
        }
    }
}
