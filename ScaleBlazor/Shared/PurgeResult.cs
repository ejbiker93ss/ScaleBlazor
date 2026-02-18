namespace ScaleBlazor.Shared;

public class PurgeResult
{
    public DateTime CutoffDate { get; set; }
    public int DeletedReadings { get; set; }
    public int DeletedPallets { get; set; }
}
