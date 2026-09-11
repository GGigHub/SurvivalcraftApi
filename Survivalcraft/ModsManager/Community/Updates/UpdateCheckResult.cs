using NuGet.Versioning;

namespace Game
{
    public class UpdateCheckResult
    {
        private string m_currentVersion;
        private string m_latestVersion;

        public int RequestId { get; set; }
        public string CurrentVersion
        {
            get => m_currentVersion;
            set
            {
                m_currentVersion = value;
                ExternalVersionParser.TryParse(value, out NuGetVersion parsed);
                CurrentNuGetVersion = parsed;
            }
        }
        public NuGetVersion CurrentNuGetVersion { get; private set; }
        public string LatestVersion
        {
            get => m_latestVersion;
            set
            {
                m_latestVersion = value;
                ExternalVersionParser.TryParse(value, out NuGetVersion parsed);
                LatestNuGetVersion = parsed;
            }
        }
        public NuGetVersion LatestNuGetVersion { get; private set; }
        public GiteeReleaseResponse Release { get; set; }
        public GiteeReleaseAsset ScmodAsset { get; set; }
        public PostItem CommunityPost { get; set; }
        public PostVersionItem CommunityVersion { get; set; }
        public PostFileItem CommunityFile { get; set; }
        public bool HasUpdate { get; set; }
        public string Error { get; set; }
    }
}
