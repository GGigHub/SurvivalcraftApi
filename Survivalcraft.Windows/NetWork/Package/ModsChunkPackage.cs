using System;
using System.Collections.Generic;
using System.IO;
using Engine;
using Game;
using Game.NetWork;

namespace Game.NetWork.Packages;

public class ModsChunkPackage : IPackage
{
    public byte ID => (byte)PackageType.ModsChunk;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.NotConnected;

    public string FileName;
    public long TotalSize;
    public long Offset;
    public byte[] Data;
    public bool IsLast;

    private static readonly Dictionary<string, Stream> s_openStreams = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> s_written = new(StringComparer.OrdinalIgnoreCase);

    public ModsChunkPackage() { }

    public ModsChunkPackage(string fileName, long totalSize, long offset, byte[] data, bool isLast)
    {
        FileName = fileName;
        TotalSize = totalSize;
        Offset = offset;
        Data = data;
        IsLast = isLast;
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        if (isServer)
            return;

        if (string.IsNullOrEmpty(FileName) || Data == null)
            return;

        string finalPath = Storage.CombinePaths(ModsManager.ModsPath, FileName);
        string partPath = finalPath + ".part";

        void Fail(string message)
        {
            CommonLib.ModTransferState.Error = message;
            try
            {
                if (s_openStreams.TryGetValue(FileName, out var fs))
                {
                    try { fs.Close(); } catch { }
                    s_openStreams.Remove(FileName);
                }
                s_written.Remove(FileName);
            }
            catch { }
            try
            {
                if (Storage.FileExists(partPath))
                    Storage.DeleteFile(partPath);
            }
            catch { }

            try
            {
                netNode.Stop(message);
            }
            catch { }
        }

        if (!s_openStreams.TryGetValue(FileName, out var s))
        {
            try
            {
                if (!Storage.DirectoryExists(ModsManager.ModsPath))
                    Storage.CreateDirectory(ModsManager.ModsPath);

                s = Storage.OpenFile(partPath, OpenFileMode.Create);
                s_openStreams[FileName] = s;
                s_written[FileName] = 0;

                CommonLib.ModTransferState.CurrentMod = FileName;
                CommonLib.ModTransferState.CurrentFileTotalBytes = TotalSize;
                CommonLib.ModTransferState.CurrentFileReceivedBytes = 0;
            }
            catch (Exception e)
            {
                Fail($"创建模组缓存文件失败:{FileName}:{e.Message}");
                return;
            }
        }

        if (!s_written.TryGetValue(FileName, out var written))
            written = 0;

        if (Offset != written)
        {
            Fail($"分块偏移不匹配:{FileName} offset={Offset} written={written}");
            return;
        }

        try
        {
            s.Write(Data, 0, Data.Length);
        }
        catch (Exception e)
        {
            Fail($"写入模组分块失败:{FileName}:{e.Message}");
            return;
        }
        written += Data.Length;
        s_written[FileName] = written;
        CommonLib.ModTransferState.CurrentMod = FileName;
        CommonLib.ModTransferState.CurrentFileTotalBytes = TotalSize;
        CommonLib.ModTransferState.CurrentFileReceivedBytes = written;

        if (IsLast)
        {
            s.Flush();
            s.Close();
            s_openStreams.Remove(FileName);
            s_written.Remove(FileName);

            string md5;
            try
            {
                md5 = ModsManager.GetNetmodDecipheredMd5(partPath);
            }
            catch (Exception e)
            {
                Fail($"计算模组MD5失败:{FileName}:{e.Message}");
                return;
            }
            if (CommonLib.ModTransferState.ExpectedMd5.TryGetValue(FileName, out var expected)
                && !string.IsNullOrEmpty(expected)
                && !string.Equals(md5, expected, StringComparison.OrdinalIgnoreCase))
            {
                Fail($"模组校验失败:{FileName}");
                return;
            }

            try
            {
                if (Storage.FileExists(finalPath))
                    Storage.DeleteFile(finalPath);
                Storage.MoveFile(partPath, finalPath);
            }
            catch (Exception e)
            {
                Fail($"安装模组失败:{FileName}:{e.Message}");
                return;
            }

            try
            {
                string cacheDir = ModsManager.ModCachePath;
                if (!Storage.DirectoryExists(cacheDir))
                    Storage.CreateDirectory(cacheDir);
                string cachePath = Storage.CombinePaths(cacheDir, FileName);
                if (Storage.FileExists(cachePath))
                    Storage.DeleteFile(cachePath);
                Storage.CopyFile(finalPath, cachePath);
            }
            catch
            {
            }

            CommonLib.ModTransferState.Done++;

            CommonLib.ModTransferState.CurrentFileTotalBytes = 0;
            CommonLib.ModTransferState.CurrentFileReceivedBytes = 0;
        }
    }

    public void ReadData(PackageStreamReader reader)
    {
        FileName = reader.ReadString();
        TotalSize = reader.ReadInt64();
        Offset = reader.ReadInt64();
        IsLast = reader.ReadBoolean();
        int len = reader.ReadInt32();
        Data = reader.ReadBytes(len);
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(FileName ?? string.Empty);
        writer.Write(TotalSize);
        writer.Write(Offset);
        writer.Write(IsLast);
        writer.Write(Data?.Length ?? 0);
        if (Data != null && Data.Length > 0)
            writer.Write(Data);
    }
}
