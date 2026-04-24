namespace Game {
    public enum ClientStage {
        NotConnected,
        Connected,
        ProjectLoaded,
        LoadTerrain,
        Playing
    }
    public enum EditableType : byte {
        MemoryBank = 0,
        TruthTable = 1,
        AdjustableDelayGate = 2,
        Battery = 3,
        Piston = 4,
        Switch = 5,
        Button = 6
    }
    public enum InteractType : byte {
        None = 0,
        Interact = 1,
        Hit = 2,
        Aim = 4,
        Dig = 8,
        EndDig = 16,
        EndAim = 32,
        CancelAim = 64
    }
    public enum WorkType {
        Local = 0,
        Server = 1,
        Client = 2
    }
}
