using System.IO;

namespace Game
{
    internal interface ICommunityUploadFileProvider
    {
        CommunityPreparedUploadFile PrepareWorld(string directoryName, string worldName);
        CommunityPreparedUploadFile PrepareLocalResource(LocalResourceItem resourceItem);
        Stream OpenRead(string filePath);
        void DeleteTemporaryFile(CommunityPreparedUploadFile uploadFile);
    }
}
