using System.IO;

namespace Game
{
    internal interface IUploadFileProvider
    {
        PreparedUploadFile PrepareWorld(string directoryName, string worldName);
        PreparedUploadFile PrepareLocalResource(LocalResourceItem resourceItem);
        Stream OpenRead(string filePath);
        void DeleteTemporaryFile(PreparedUploadFile uploadFile);
    }
}
