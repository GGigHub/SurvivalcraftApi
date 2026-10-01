namespace Game {
    interface IRemoteImportHost {
        bool HasExactLocalContent(ExternalContentType type, string fileName);

        bool IsModpackDataFilePresent(ModpackItem item);

        void ImportModpackDataFileSafe(byte[] data, ModpackItem item);
    }
}