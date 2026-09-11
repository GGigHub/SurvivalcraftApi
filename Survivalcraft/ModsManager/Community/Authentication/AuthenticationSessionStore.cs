using System;

namespace Game
{
    internal sealed class AuthenticationSessionStore
    {
        private readonly string m_path;

        public AuthenticationSessionStore(string path)
        {
            m_path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public Exception TrySave(string token, UserData user)
        {
            try
            {
                CredentialStore.SaveLegacyFile(m_path, token, user);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public AuthenticationSessionLoadResult Load()
        {
            try
            {
                LoginData data = CredentialStore.Load(m_path, out bool legacyFormat);
                return new AuthenticationSessionLoadResult
                {
                    Data = data,
                    IsLegacyFormat = legacyFormat
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationSessionLoadResult { Error = ex };
            }
        }

        public Exception TryDelete()
        {
            try
            {
                CredentialStore.DeleteLegacyFile(m_path);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public Exception TrySaveForServer(string apiBaseUrl, string token, UserData user)
        {
            try
            {
                CredentialStore.Save(apiBaseUrl, token, user);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public LoginData LoadForServer(string apiBaseUrl)
        {
            return CredentialStore.Load(apiBaseUrl);
        }

        public Exception TryDeleteForServer(string apiBaseUrl)
        {
            try
            {
                CredentialStore.Delete(apiBaseUrl);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public LoginData LoadLegacy(out bool legacyFormat)
        {
            return CredentialStore.LoadLegacy(out legacyFormat);
        }

        public Exception TryDeleteLegacy()
        {
            try
            {
                CredentialStore.DeleteLegacy();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }

    internal sealed class AuthenticationSessionLoadResult
    {
        public LoginData Data { get; set; }
        public bool IsLegacyFormat { get; set; }
        public Exception Error { get; set; }
    }
}
