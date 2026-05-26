

namespace Game.Network;

public class SubsystemSeasonPacket : Packet {
    public override byte ID => (byte)PacketType.Season;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public Season SeasonNet { get; set; }
    public int SeasonIndexNet;
    public float TimeOfSeasonNet { get; set; }

    public SubsystemSeasonPacket() {
    }

    public SubsystemSeasonPacket(int seasonIndex, float timeofseason) {
        SeasonIndexNet = seasonIndex;
        TimeOfSeasonNet = timeofseason;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SeasonIndexNet);
        float timeOfSeasonNet = TimeOfSeasonNet;
        archive.Value(ref timeOfSeasonNet);
        TimeOfSeasonNet = timeOfSeasonNet;
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemSeasons weather = GameManager.Project.FindSubsystem<SubsystemSeasons>();
        weather.Season = (Season)SeasonIndexNet;
        weather.TimeOfSeason = TimeOfSeasonNet;
    }
}

