using System;

namespace Game
{
    /// <summary>
    /// 按归一化服务器地址读写各服凭据文件（data:ScbbsPlus/auth/&lt;sha256&gt;.bin，v4）；
    /// legacy auth.bin 仅供一次性迁移。
    /// </summary>
    internal sealed class AuthenticationSessionStore
    {
        public Exception TrySave(string apiBaseUrl, string token, UserData user)
        {
            try
            {
                CommunityCredentialStore.Save(apiBaseUrl, token, user);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public LoginData Load(string apiBaseUrl)
        {
            return CommunityCredentialStore.Load(apiBaseUrl);
        }

        public Exception TryDelete(string apiBaseUrl)
        {
            try
            {
                CommunityCredentialStore.Delete(apiBaseUrl);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
