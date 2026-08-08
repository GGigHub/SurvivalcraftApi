using Engine;

namespace Game.Network;

public class TerrainSerializerNet : TerrainSerializer23 {

    static readonly string path = ModsManager.IsAndroid
        ? Storage.CombinePaths(RunPath.AndroidFilePath, "NetChunks.tmp")
        : Storage.CombinePaths("app:/", "NetChunks24");

    public TerrainSerializerNet() {
        if (Storage.DirectoryExists(path)) Delete(path);
        else Storage.CreateDirectory(path);
        m_storage = new RegionFileStorage();
        m_storage.Open(path, "");
    }

    public override void Dispose() {
        base.Dispose();
        if (Storage.DirectoryExists(path)) {
            Delete(path);
        }
    }
    /// <summary>
    /// 轮训删除文件夹下的文件
    /// </summary>
    /// <param name="path"></param>
    public void Delete(string path) {
        foreach (var d in Storage.ListDirectoryNames(path)) {
            var dd = Storage.CombinePaths(path, d);
            Delete(dd);
        }
        foreach (var f in Storage.ListFileNames(path)) {
            var ff = Storage.CombinePaths(path, f);
            Storage.DeleteFile(ff);
        }
    }
}


