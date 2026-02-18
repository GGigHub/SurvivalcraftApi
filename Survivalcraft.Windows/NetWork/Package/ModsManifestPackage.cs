using System;
using System.Collections.Generic;
using System.Linq;
using Engine;
using Game;
using Game.NetWork;

namespace Game.NetWork.Packages;

public class ModsManifestPackage : IPackage
{
    public byte ID => (byte)PackageType.ModsManifest;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.NotConnected;

    public class ManifestItem
    {
        public string PackageName;
        public string Version;
        public string Md5;
        public long Size;
        public string FileName;
    }

    public List<ManifestItem> Items = new();

    public ModsManifestPackage() { }

    public ModsManifestPackage(IEnumerable<ManifestItem> items)
    {
        Items = items?.ToList() ?? new List<ManifestItem>();
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        if (isServer)
            return;

        CommonLib.ModTransferState.Reset();
        CommonLib.ModTransferState.InProgress = true;

        foreach (var item in Items)
        {
            if (item == null || string.IsNullOrEmpty(item.FileName))
                continue;
            CommonLib.ModTransferState.ExpectedMd5[item.FileName] = item.Md5 ?? string.Empty;
        }

        static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && slash < path.Length - 1)
                return path.Substring(slash + 1);
            return path;
        }

        string cacheDir = ModsManager.ModCachePath;
        if (!Storage.DirectoryExists(cacheDir))
            Storage.CreateDirectory(cacheDir);

        string modsDir = ModsManager.ModsPath;
        if (!Storage.DirectoryExists(modsDir))
            Storage.CreateDirectory(modsDir);

        // Build manifest map by file name -> expected md5
        var manifest = Items
            .Where(i => i != null && !string.IsNullOrEmpty(i.FileName))
            .GroupBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Md5 ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        // Local mods currently in NetMods, by file name.
        var localMods = ModsManager.ModList
            .Where(m => !m.IsSystemMod && m.modInfo.PackageName != "survivalcraft")
            .ToDictionary(m => GetFileName(m.ModFilePath), m => m, StringComparer.OrdinalIgnoreCase);

        static bool IsExpected(string actual, string expected)
        {
            return !string.IsNullOrEmpty(expected) && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        // Move extra/mismatching local mods into cache to enforce strict alignment.
        foreach (var kv in localMods)
        {
            string fileName = kv.Key;
            var mod = kv.Value;

            bool shouldKeep = false;
            if (manifest.TryGetValue(fileName, out var expectedMd5))
            {
                shouldKeep = IsExpected(mod.ResourcesMd5 ?? string.Empty, expectedMd5);
            }

            if (!shouldKeep)
            {
                string src = mod.ModFilePath;
                if (!string.IsNullOrEmpty(src) && Storage.FileExists(src))
                {
                    string dst = Storage.CombinePaths(cacheDir, fileName);
                    if (Storage.FileExists(dst))
                        Storage.DeleteFile(dst);
                    Storage.MoveFile(src, dst);
                }
            }
        }

        // Cache hit by same file name + same md5.
        var need = new List<string>();
        foreach (var kv in manifest)
        {
            string fileName = kv.Key;
            string expectedMd5 = kv.Value;

            // 1) If NetMods already has correct file, hit.
            if (localMods.TryGetValue(fileName, out var localMod) && IsExpected(localMod.ResourcesMd5 ?? string.Empty, expectedMd5))
                continue;

            // 2) If ModsCache has same-named file and md5 matches, restore.
            string cachePath = Storage.CombinePaths(cacheDir, fileName);
            if (Storage.FileExists(cachePath))
            {
                try
                {
                    string cacheMd5 = ModsManager.GetNetmodDecipheredMd5(cachePath);
                    if (IsExpected(cacheMd5, expectedMd5))
                    {
                        string finalPath = Storage.CombinePaths(modsDir, fileName);
                        if (Storage.FileExists(finalPath))
                            Storage.DeleteFile(finalPath);
                        Storage.CopyFile(cachePath, finalPath);
                        continue;
                    }
                }
                catch
                {
                }
            }

            need.Add(fileName);
        }

        CommonLib.ModTransferState.Total = need.Count;

        // Always send Need (even empty) to let server respond with ModsDone.
        netNode.SendWriterFromPackage(new ModsNeedPackage(need), From?.Peer, false);
    }

    public void ReadData(PackageStreamReader reader)
    {
        byte count = reader.ReadByte();
        Items = new List<ManifestItem>(count);
        for (int i = 0; i < count; i++)
        {
            var it = new ManifestItem();
            it.PackageName = reader.ReadString();
            it.Version = reader.ReadString();
            it.Md5 = reader.ReadString();
            it.Size = reader.ReadInt64();
            it.FileName = reader.ReadString();
            Items.Add(it);
        }
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write((byte)Math.Min(255, Items.Count));
        int count = Math.Min(255, Items.Count);
        for (int i = 0; i < count; i++)
        {
            var it = Items[i];
            writer.Write(it?.PackageName ?? string.Empty);
            writer.Write(it?.Version ?? string.Empty);
            writer.Write(it?.Md5 ?? string.Empty);
            writer.Write(it?.Size ?? 0L);
            writer.Write(it?.FileName ?? string.Empty);
        }
    }
}
