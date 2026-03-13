namespace Game.NetWork;

public class ModTransferState
{
    public bool InProgress;
    public int Total;
    public int Done;
    public string CurrentMod = string.Empty;
    public string Error = string.Empty;
    public System.Collections.Generic.Dictionary<string, string> ExpectedMd5 = new(System.StringComparer.OrdinalIgnoreCase);
    public string TargetServer = string.Empty;
    public string TargetPassword = string.Empty;

    public long CurrentFileTotalBytes;
    public long CurrentFileReceivedBytes;

    public void Reset()
    {
        InProgress = false;
        Total = 0;
        Done = 0;
        CurrentMod = string.Empty;
        Error = string.Empty;
        ExpectedMd5.Clear();

        CurrentFileTotalBytes = 0;
        CurrentFileReceivedBytes = 0;
    }
}
