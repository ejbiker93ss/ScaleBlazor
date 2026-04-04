namespace ScaleBlazor.Shared;

public class ScaleDebugState
{
    public bool AutoCaptureEnabled { get; set; }
    public bool AutoReadLocked { get; set; }
    public bool SeenZeroWhileLocked { get; set; }
    public int StableReadCount { get; set; }
    public int RecentCount { get; set; }
    public double CurrentWeight { get; set; }
    public double LastExactWeight { get; set; }
    public double RecentAverage { get; set; }
    public double RecentMin { get; set; }
    public double RecentMax { get; set; }
    public double PercentDiff { get; set; }
    public double RangePercent { get; set; }
    public double AutoCaptureThresholdPercent { get; set; }
    public int SavedReadingsCount { get; set; }
    public double SavedAverage { get; set; }
    public double SavedPercentDiff { get; set; }
    public double ExactHoldElapsedMs { get; set; }
    public double ExactHoldRequiredMs { get; set; }
    public bool PassStabilityCheck { get; set; }
    public bool PassHoldCheck { get; set; }
    public bool PassSavedThresholdCheck { get; set; }
    public bool ReadyToAutoCapture { get; set; }
}
