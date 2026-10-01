namespace Game {
    interface ICommunityUploadFileProvider {
        CommunityPreparedUploadFile PrepareWorld(string directoryName, string worldName);
        CommunityPreparedUploadFile PrepareLocalResource(LocalResourceItem resourceItem);
        Stream OpenRead(string filePath);
        void DeleteTemporaryFile(CommunityPreparedUploadFile uploadFile);
    }
}