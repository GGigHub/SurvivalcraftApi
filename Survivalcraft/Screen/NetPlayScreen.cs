namespace Game;

public class NetPlayScreen : Screen {
    public enum ConnectState {
        Unavaliable,
        Checking,
        Avaliable
    }

    public class Connect {
        public string name;
        public string ip;
        public string version;
        public ushort playerCount;
        public ConnectState state;
        public float TimeOfDay;
        public long usedTime;
        public GameMode gameMode;
        public bool HasPassword;
        public ushort maxCount;
        public bool fromBroadcast;
        public bool fromLocal;
        public bool fromCommunity;
        public bool IsLocalRemote;
        public Season season;
        public float timeOfSeason;
        public bool isLegacyVersion;

        public override int GetHashCode() => (ip ?? string.Empty).GetHashCode();
        public override bool Equals(object obj) => obj is Connect c && c.ip == ip;
        public override string ToString() => name ?? string.Empty;
    }

    public static Dictionary<string, string> IpToDNS = new();
    public static Dictionary<string, string> DNSToName = new();

    public bool CheckConnectExists(Connect connect, out Connect found) {
        found = null;
        return false;
    }

    public bool CheckSaveConnectExists(Connect connect, out Connect found) {
        found = null;
        return false;
    }

    public void UpdateList() {
    }
}
