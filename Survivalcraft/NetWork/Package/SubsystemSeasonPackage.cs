namespace Game.NetWork.Packages;

public class SubsystemSeasonPackage : IPackage {
    public override byte ID => (byte)PackageType.Season;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public Season SeasonNet { get; set; }
    public int SeasonIndexNet;
    public float TimeOfSeasonNet { get; set; }

    public SubsystemSeasonPackage() {
    }

    public SubsystemSeasonPackage(int seasonIndex, float timeofseason) {
        SeasonIndexNet = seasonIndex;
        TimeOfSeasonNet = timeofseason;
    }

    public override void Serialize(PackageSerializer archive) {
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
