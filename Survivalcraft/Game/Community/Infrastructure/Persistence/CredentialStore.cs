using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engine;

namespace Game
{
    /// <summary>
    /// 负责凭据文件的加密、解密和旧格式迁移。
    ///
    /// 当前格式（v4）：每服一文件 data:ScbbsPlus/auth/&lt;sha256(归一化地址)&gt;.bin，魔数 SCBPAUTH4，
    /// payload 为 JSON {apiBaseUrl, login}（自描述）；加密方式同 v3：
    /// - Windows：魔数 + DPAPI（CurrentUser 作用域，带应用专属 entropy）密文。
    /// - 其他平台：魔数 + salt(16) + nonce(12) + AES-GCM 密文 + tag(16)，
    ///   密钥由 PBKDF2-SHA256(600k) 派生。
    ///
    /// 仍可读取的旧格式（仅 legacy auth.bin 迁移路径）：SCBPAUTH3/SCBPAUTH2/无魔数 AES-CBC：
    /// - 魔数 SCBPAUTH3：v3 单文件（Windows DPAPI / 非 Windows AES-GCM）。
    /// - 魔数 SCBPAUTH2：旧版 Windows DPAPI（无 entropy）。
    /// - 无魔数：旧版非 Windows AES-CBC（MachineName + 固定 salt + 1 万次 PBKDF2）。
    ///
    /// 威胁模型：非 Windows 路径的密钥派生口令为 MachineName（非秘密），仅隔离不知机器名的访问者，
    /// 不抵抗知道机器名的离线攻击；Windows 路径走 DPAPI，安全性由操作系统保障。
    /// </summary>
    internal static class CredentialStore
    {
        private static readonly byte[] s_fileMagic = Encoding.ASCII.GetBytes("SCBPAUTH4");
        private static readonly byte[] s_dpapiEntropy = Encoding.UTF8.GetBytes("ScbbsPlus.CredentialStore.v4");
        // legacy 单文件（auth.bin）：v3 及更早格式，仅供 LoadLegacy 一次性迁移读取。
        private static readonly byte[] s_legacyFileMagic = Encoding.ASCII.GetBytes("SCBPAUTH3");
        private static readonly byte[] s_legacyDpapiMagic = Encoding.ASCII.GetBytes("SCBPAUTH2");
        private static readonly byte[] s_legacyDpapiEntropy = Encoding.UTF8.GetBytes("ScbbsPlus.CredentialStore.v3");

        private const int s_pbkdf2Iterations = 600_000;
        private const int s_saltSize = 16;
        private const int s_nonceSize = 12;
        private const int s_tagSize = 16;
        private const int s_keySize = 32;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        // ===== 每服一文件（v4）=====

        private static string GetCredentialPath(string apiBaseUrl)
        {
            string normalized = CommunityServerManager.NormalizeApiBaseUrl(apiBaseUrl);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            var hex = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return Storage.CombinePaths(CommunityStoragePaths.AuthDirectory, hex + ".bin");
        }

        public static void Save(string apiBaseUrl, string token, UserData user)
        {
            string path = GetCredentialPath(apiBaseUrl);
            string directory = Storage.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Storage.CreateDirectory(directory);
            }
            string json = JsonSerializer.Serialize(new StoredCredential
            {
                ApiBaseUrl = CommunityServerManager.NormalizeApiBaseUrl(apiBaseUrl),
                Login = new LoginData { Token = token, User = user }
            });
            byte[] encrypted = EncryptString(json);
            AtomicWriteAllBytes(path, encrypted);
        }

        public static LoginData Load(string apiBaseUrl)
        {
            string path = GetCredentialPath(apiBaseUrl);
            if (!Storage.FileExists(path))
            {
                // 活动文件缺失（如原子写硬崩溃后仅剩 .bak）时回退读取备份。
                string backupPath = path + ".bak";
                if (!Storage.FileExists(backupPath))
                {
                    return null;
                }
                path = backupPath;
            }

            byte[] encrypted = Storage.ReadAllBytes(path);
            string json;
            try
            {
                json = DecryptCurrentFileBytes(encrypted);
            }
            catch (CryptographicException ex)
            {
                // 本平台损坏（截断/坏 GCM tag/坏 DPAPI blob）：作废文件，用户干净重登后由 Save 重建。
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCredentialStore", "DecryptCredentialFileFailed"), ex.Message));
                TryDeleteFile(path);
                TryDeleteFile(path + ".bak");
                return null;
            }
            if (json == null)
            {
                TryDeleteFile(path);
                TryDeleteFile(path + ".bak");
                return null;
            }
            StoredCredential stored;
            try
            {
                stored = JsonSerializer.Deserialize<StoredCredential>(json);
            }
            catch (JsonException ex)
            {
                // 可解密但 payload 非法（截断/手改）：与损坏文件同待，作废后由 Save 重建。
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCredentialStore", "DecryptCredentialFileFailed"), ex.Message));
                TryDeleteFile(path);
                TryDeleteFile(path + ".bak");
                return null;
            }
            if (stored?.Login == null)
            {
                return null;
            }
            if (!string.Equals(stored.ApiBaseUrl, CommunityServerManager.NormalizeApiBaseUrl(apiBaseUrl), StringComparison.OrdinalIgnoreCase))
            {
                // 文件名与 payload 归属不一致（正常不可能，防哈希碰撞/手拷文件）：按未找到处理。
                Log.Warning($"[ScbbsPlus] Credential file server mismatch: {stored.ApiBaseUrl} != {CommunityServerManager.NormalizeApiBaseUrl(apiBaseUrl)}.");
                return null;
            }
            return stored.Login;
        }

        public static void Delete(string apiBaseUrl)
        {
            // 登出须同时清理备份与临时文件：Load 在活动文件缺失时会回退 .bak。
            string path = GetCredentialPath(apiBaseUrl);
            TryDeleteFile(path);
            TryDeleteFile(path + ".bak");
            TryDeleteFile(path + ".tmp");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (Storage.FileExists(path))
                {
                    Storage.DeleteFile(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCredentialStore", "DeleteCredentialFileFailed"), path, ex.Message));
            }
        }

        // 先完整写临时文件，再备份-替换-回滚，避免直接覆写活动文件导致中途崩溃产生截断/损坏。
        private static void AtomicWriteAllBytes(string path, byte[] bytes)
        {
            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";
            Storage.WriteAllBytes(temporaryPath, bytes);

            if (!Storage.FileExists(path))
            {
                Storage.MoveFile(temporaryPath, path);
                return;
            }

            // 备份旧文件 → 替换 → 删除备份；替换失败则尝试回滚，保证至少有一份可用凭据。
            Storage.DeleteFile(backupPath);
            try
            {
                Storage.MoveFile(path, backupPath);
            }
            catch
            {
                // 备份建立失败（如反病毒锁住旧文件）：直接尝试用临时文件替换活动文件
                // （MoveFile 自身先删目标再移动）；失败则把新凭据落到 .bak ——
                // Load 在活动文件缺失时会回退 .bak，保证凭据至少有一份可读。
                try
                {
                    Storage.MoveFile(temporaryPath, path);
                    return;
                }
                catch
                {
                    try
                    {
                        Storage.MoveFile(temporaryPath, backupPath);
                    }
                    catch
                    {
                        // .tmp 仍保留在磁盘上，登录流程未丢失数据。
                    }
                    throw;
                }
            }
            try
            {
                Storage.MoveFile(temporaryPath, path);
                Storage.DeleteFile(backupPath);
            }
            catch
            {
                try
                {
                    if (!Storage.FileExists(path))
                    {
                        Storage.MoveFile(backupPath, path);
                    }
                }
                catch
                {
                    // 回滚也失败时只能放弃，交由调用方捕获并提示重新登录。
                }
                throw;
            }
        }

        private static byte[] EncryptString(string plainText)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    byte[] protectedBytes = ProtectWithDpapi(plainBytes, s_dpapiEntropy);
                    byte[] result = new byte[s_fileMagic.Length + protectedBytes.Length];
                    Buffer.BlockCopy(s_fileMagic, 0, result, 0, s_fileMagic.Length);
                    Buffer.BlockCopy(protectedBytes, 0, result, s_fileMagic.Length, protectedBytes.Length);
                    return result;
                }

                return EncryptWithAesGcm(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        private static string DecryptCurrentFileBytes(byte[] data)
        {
            if (!StartsWithMagic(data, s_fileMagic))
            {
                // 每服文件只认 v4；v3 内容只可能出现在 legacy auth.bin（LoadLegacy 路径）。
                throw new CryptographicException(LanguageControl.Get("ScbbsPlusLoginDialog", "CredentialFileFormatInvalid"));
            }
            byte[] body = new byte[data.Length - s_fileMagic.Length];
            Buffer.BlockCopy(data, s_fileMagic.Length, body, 0, body.Length);
            byte[] plainBytes = OperatingSystem.IsWindows()
                ? UnprotectWithDpapi(body, s_dpapiEntropy)
                : DecryptWithAesGcm(body);
            try
            {
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        internal static byte[] EncryptWithAesGcm(byte[] plainBytes)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(s_saltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(s_nonceSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                Environment.MachineName, salt, s_pbkdf2Iterations, HashAlgorithmName.SHA256, s_keySize);
            try
            {
                byte[] ciphertext = new byte[plainBytes.Length];
                byte[] tag = new byte[s_tagSize];
                using AesGcm aes = new(key, s_tagSize);
                aes.Encrypt(nonce, plainBytes, ciphertext, tag);
                byte[] result = new byte[
                    s_fileMagic.Length + salt.Length + nonce.Length + ciphertext.Length + tag.Length];
                int offset = 0;
                Buffer.BlockCopy(s_fileMagic, 0, result, offset, s_fileMagic.Length);
                offset += s_fileMagic.Length;
                Buffer.BlockCopy(salt, 0, result, offset, salt.Length);
                offset += salt.Length;
                Buffer.BlockCopy(nonce, 0, result, offset, nonce.Length);
                offset += nonce.Length;
                Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);
                offset += ciphertext.Length;
                Buffer.BlockCopy(tag, 0, result, offset, tag.Length);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static string DecryptBytes(byte[] data, out bool legacyFormat)
        {
            if (StartsWithMagic(data, s_legacyFileMagic))
            {
                legacyFormat = false;
                byte[] body = new byte[data.Length - s_legacyFileMagic.Length];
                Buffer.BlockCopy(data, s_legacyFileMagic.Length, body, 0, body.Length);
                byte[] plainBytes = OperatingSystem.IsWindows()
                    ? UnprotectWithDpapi(body, s_legacyDpapiEntropy)
                    : DecryptWithAesGcm(body);
                try
                {
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }

            // 旧版 Windows DPAPI（无 entropy）：迁移读取。
            if (StartsWithMagic(data, s_legacyDpapiMagic))
            {
                if (VersionsManager.CurrentPlatform != VersionsManager.Platform.Windows)
                {
                    // 跨平台迁移：DPAPI 凭据无法在本平台解开，返回 null 由 Load 作废文件。
                    legacyFormat = true;
                    return null;
                }

                legacyFormat = true;
                byte[] body = new byte[data.Length - s_legacyDpapiMagic.Length];
                Buffer.BlockCopy(data, s_legacyDpapiMagic.Length, body, 0, body.Length);
                byte[] plainBytes = UnprotectWithDpapi(body, null);
                try
                {
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }

            // 旧版非 Windows AES-CBC：迁移读取。
            legacyFormat = true;
            return DecryptLegacyBytes(data);
        }

        internal static byte[] DecryptWithAesGcm(byte[] data)
        {
            int headerSize = s_saltSize + s_nonceSize;
            if (data == null || data.Length < headerSize + s_tagSize)
            {
                throw new CryptographicException(LanguageControl.Get("ScbbsPlusLoginDialog", "CredentialFileFormatInvalid"));
            }

            byte[] salt = new byte[s_saltSize];
            byte[] nonce = new byte[s_nonceSize];
            Buffer.BlockCopy(data, 0, salt, 0, s_saltSize);
            Buffer.BlockCopy(data, s_saltSize, nonce, 0, s_nonceSize);
            int ciphertextLength = data.Length - headerSize - s_tagSize;
            if (ciphertextLength < 0)
            {
                throw new CryptographicException(LanguageControl.Get("ScbbsPlusLoginDialog", "CredentialFileFormatInvalid"));
            }

            byte[] ciphertext = new byte[ciphertextLength];
            byte[] tag = new byte[s_tagSize];
            Buffer.BlockCopy(data, headerSize, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(data, headerSize + ciphertextLength, tag, 0, s_tagSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                Environment.MachineName, salt, s_pbkdf2Iterations, HashAlgorithmName.SHA256, s_keySize);
            try
            {
                byte[] plainBytes = new byte[ciphertextLength];
                using AesGcm aes = new(key, s_tagSize);
                aes.Decrypt(nonce, ciphertext, tag, plainBytes);
                return plainBytes;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static byte[] ProtectWithDpapi(byte[] data, byte[] entropy)
        {
            DataBlob input = CreateDataBlob(data);
            DataBlob output = default;
            DataBlob entropyBlob;
            IntPtr entropyBlobPtr = AllocEntropyBlob(entropy, out entropyBlob);
            try
            {
                if (!CryptProtectData(ref input, "ScbbsPlus 登录凭据", entropyBlobPtr, IntPtr.Zero, IntPtr.Zero, 1, out output))
                {
                    throw new CryptographicException(Marshal.GetLastWin32Error());
                }

                return CopyDataBlob(output);
            }
            finally
            {
                FreeEntropyBlob(entropyBlobPtr);
                FreeInputDataBlob(entropyBlob);
                FreeInputDataBlob(input);
                FreeOutputDataBlob(output);
            }
        }

        private static byte[] UnprotectWithDpapi(byte[] data, byte[] entropy)
        {
            DataBlob input = CreateDataBlob(data);
            DataBlob output = default;
            DataBlob entropyBlob;
            IntPtr entropyBlobPtr = AllocEntropyBlob(entropy, out entropyBlob);
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, entropyBlobPtr, IntPtr.Zero, IntPtr.Zero, 1, out output))
                {
                    throw new CryptographicException(Marshal.GetLastWin32Error());
                }

                return CopyDataBlob(output);
            }
            finally
            {
                FreeEntropyBlob(entropyBlobPtr);
                FreeInputDataBlob(entropyBlob);
                FreeInputDataBlob(input);
                FreeOutputDataBlob(output);
            }
        }

        // DPAPI 的 optionalEntropy 需要指向一个 native DATA_BLOB 结构。这里把托管 DataBlob
        // 复制到非托管内存并返回其指针；调用方负责用 FreeEntropyBlob 释放。
        private static IntPtr AllocEntropyBlob(byte[] entropy, out DataBlob managed)
        {
            managed = default;
            if (entropy == null || entropy.Length == 0)
            {
                return IntPtr.Zero;
            }

            managed = CreateDataBlob(entropy);
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<DataBlob>());
            Marshal.StructureToPtr(managed, ptr, false);
            return ptr;
        }

        private static void FreeEntropyBlob(IntPtr entropyBlobPtr)
        {
            // StructureToPtr 只复制结构体本身，释放容器指针即可；缓冲区由 FreeInputDataBlob(managed) 释放。
            if (entropyBlobPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure<DataBlob>(entropyBlobPtr);
                Marshal.FreeHGlobal(entropyBlobPtr);
            }
        }

        private static DataBlob CreateDataBlob(byte[] data)
        {
            DataBlob blob = new()
            {
                Size = data?.Length ?? 0,
                Data = data == null || data.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(data.Length)
            };
            if (blob.Data != IntPtr.Zero)
            {
                Marshal.Copy(data, 0, blob.Data, data.Length);
            }
            return blob;
        }

        private static byte[] CopyDataBlob(DataBlob blob)
        {
            if (blob.Data == IntPtr.Zero || blob.Size <= 0)
            {
                return [];
            }

            byte[] result = new byte[blob.Size];
            Marshal.Copy(blob.Data, result, 0, result.Length);
            return result;
        }

        private static void FreeInputDataBlob(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero)
            {
                // 释放前清零，避免明文/密钥在非托管堆残留。
                for (int i = 0; i < blob.Size; i++)
                {
                    Marshal.WriteByte(blob.Data, i, 0);
                }
                Marshal.FreeHGlobal(blob.Data);
            }
        }

        private static void FreeOutputDataBlob(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero)
            {
                // 释放前清零：DPAPI 解密输出是明文，避免在非托管堆残留。
                for (int i = 0; i < blob.Size; i++)
                {
                    Marshal.WriteByte(blob.Data, i, 0);
                }
                LocalFree(blob.Data);
            }
        }

        private static bool StartsWithMagic(byte[] data, byte[] magic)
        {
            if (data == null || data.Length <= magic.Length)
            {
                return false;
            }

            for (int i = 0; i < magic.Length; i++)
            {
                if (data[i] != magic[i])
                {
                    return false;
                }
            }
            return true;
        }

        // ===== 旧格式迁移读取（只读，不再用于写入）=====

        private static byte[] DeriveLegacyEncryptionKey()
        {
            byte[] salt = Encoding.UTF8.GetBytes("ScbbsPlus.Salt@2026!v1");
            return Rfc2898DeriveBytes.Pbkdf2(
                Environment.MachineName, salt, 10_000, HashAlgorithmName.SHA256, 32);
        }

        private static string DecryptLegacyBytes(byte[] data)
        {
            const int ivLength = 16;
            if (data == null || data.Length <= ivLength || (data.Length - ivLength) % ivLength != 0)
            {
                throw new CryptographicException(LanguageControl.Get("ScbbsPlusLoginDialog", "OldCredentialFileFormatInvalid"));
            }

            byte[] key = DeriveLegacyEncryptionKey();
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;

                byte[] iv = new byte[ivLength];
                byte[] cipherBytes = new byte[data.Length - ivLength];
                Buffer.BlockCopy(data, 0, iv, 0, ivLength);
                Buffer.BlockCopy(data, ivLength, cipherBytes, 0, cipherBytes.Length);

                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor();
                byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                try
                {
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
