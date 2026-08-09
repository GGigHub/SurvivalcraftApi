using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network;

public abstract class SkinPacketBase : Packet {
    public const int MaximumSkinNameUtf8Bytes = 256;
    public const int MaximumSkinDataBytes = 4 * 1024 * 1024;
    public const int MaximumSkinDimension = 4096;
    public const long MaximumSkinPixels = 16L * 1024 * 1024;

    public static byte[] LoadSkinData(string skinName) {
        if (!TryValidateSkinName(skinName, out string normalizedName)
            || !CharacterSkinsManager.HasSkinRes(normalizedName)) {
            return null;
        }
        try {
            using Stream stream = Storage.OpenFile(
                Storage.CombinePaths(
                    ModsManager.CharacterSkinsDirectoryName,
                    normalizedName),
                OpenFileMode.Read);
            byte[] data = ModsManager.StreamToBytes(stream);
            return TryValidateSkinData(data) ? data : null;
        }
        catch {
            return null;
        }
    }

    public static void SerializeSkinData(PacketSerializer archive, ref byte[] skinData) {
        archive.Buffer(ref skinData, MaximumSkinDataBytes);
    }

    public static bool SaveSkin(string skinName, byte[] skinData) {
        if (!TryValidateSkinName(skinName, out string normalizedName)
            || !TryValidateSkinData(skinData)) {
            return false;
        }
        CharacterSkinsManager.waitReplyList.RemoveAll(name =>
            string.Equals(name, normalizedName, StringComparison.OrdinalIgnoreCase));
        CharacterSkinsManager.SaveSkinToFile(normalizedName, skinData);
        if (!CharacterSkinsManager.m_characterSkinNames.Contains(
                normalizedName,
                StringComparer.OrdinalIgnoreCase)) {
            CharacterSkinsManager.m_characterSkinNames.Add(normalizedName);
        }
        return true;
    }

    public static bool TryValidateSkinName(
        string skinName,
        out string normalizedName) {
        normalizedName = skinName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0
            || !string.Equals(normalizedName, skinName, StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(normalizedName) > MaximumSkinNameUtf8Bytes
            || CharacterSkinsManager.IsBuiltIn(normalizedName)
            || normalizedName.Contains('/')
            || normalizedName.Contains('\\')
            || normalizedName.Contains(':')
            || normalizedName.Any(char.IsControl)
            || !string.Equals(
                Path.GetExtension(normalizedName),
                ".scskin",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(normalizedName),
                normalizedName,
                StringComparison.Ordinal)) {
            normalizedName = string.Empty;
            return false;
        }
        return true;
    }

    public static bool TryValidateSkinData(byte[] skinData) {
        if (skinData == null
            || skinData.Length == 0
            || skinData.Length > MaximumSkinDataBytes) {
            return false;
        }
        try {
            using MemoryStream stream = new(skinData, writable: false);
            SixLabors.ImageSharp.ImageInfo info =
                SixLabors.ImageSharp.Image.Identify(stream);
            if (info == null
                || info.Width <= 0
                || info.Height <= 0
                || info.Width > MaximumSkinDimension
                || info.Height > MaximumSkinDimension
                || (long)info.Width * info.Height > MaximumSkinPixels) {
                return false;
            }
            stream.Position = 0;
            CharacterSkinsManager.ValidateCharacterSkin(stream);
            return true;
        }
        catch {
            return false;
        }
    }
}
