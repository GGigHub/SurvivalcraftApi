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
                CredentialStore.Save(m_path, token, user);
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
                CredentialStore.Delete(m_path);
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
