namespace ScaleBlazor.Shared;

public class AppSettings
{
    public int Id { get; set; }
    public int ReadingsPerPallet { get; set; } = 10;
    public bool AutoCaptureEnabled { get; set; } = false;
    public double AutoCaptureThresholdPercent { get; set; } = 1.0;
    public string? ScalePortName { get; set; }
}
