using System;
using System.Collections.Generic;
using System.Linq;
using Engine;
using Game;
using Game.NetWork;

namespace Game.NetWork.Packages;

public class ModsNeedPackage : IPackage
{
    public byte ID => (byte)PackageType.ModsNeed;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.NotConnected;

    public List<string> Need = new();

    public ModsNeedPackage() { }

    public ModsNeedPackage(IEnumerable<string> need)
    {
        Need = need?.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        if (!isServer)
            return;

        if (Need == null || Need.Count == 0)
        {
            netNode.SendWriterFromPackage(new ModsDonePackage(true, string.Empty), From?.Peer, false);
            return;
        }

        const int chunkSize = 32 * 1024;

        static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && slash < path.Length - 1)
                return path.Substring(slash + 1);
            return path;
        }

        foreach (string fileName in Need)
        {
            try
            {
                var mod = ModsManager.ModList
                    .FirstOrDefault(m => !m.IsSystemMod && m.modInfo.PackageName != "survivalcraft" && string.Equals(GetFileName(m.ModFilePath), fileName, StringComparison.OrdinalIgnoreCase));

                if (mod == null)
                {
                    netNode.SendWriterFromPackage(new ModsDonePackage(false, $"服务端找不到模组文件:{fileName}"), From?.Peer, false);
                    return;
                }

                string path = mod.ModFilePath;
                long totalSize = Storage.GetFileSize(path);

                using (var s = Storage.OpenFile(path, OpenFileMode.Read))
                {
                    long offset = 0;
                    byte[] buffer = new byte[chunkSize];
                    int read;
                    while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        byte[] data = buffer;
                        if (read != buffer.Length)
                        {
                            data = new byte[read];
                            Array.Copy(buffer, 0, data, 0, read);
                        }
                        bool isLast = (offset + read) >= totalSize;
                        netNode.SendWriterFromPackage(new ModsChunkPackage(fileName, totalSize, offset, data, isLast), From?.Peer, false);
                        offset += read;
                    }
                }
            }
            catch (Exception e)
            {
                netNode.SendWriterFromPackage(new ModsDonePackage(false, $"传输模组失败:{fileName}:{e.Message}"), From?.Peer, false);
                return;
            }
        }

        netNode.SendWriterFromPackage(new ModsDonePackage(true, string.Empty), From?.Peer, false);
    }

    public void ReadData(PackageStreamReader reader)
    {
        byte count = reader.ReadByte();
        Need = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            Need.Add(reader.ReadString());
        }
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write((byte)Math.Min(255, Need.Count));
        int count = Math.Min(255, Need.Count);
        for (int i = 0; i < count; i++)
        {
            writer.Write(Need[i] ?? string.Empty);
        }
    }
}
