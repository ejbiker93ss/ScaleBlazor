namespace ScaleBlazor.Shared;

public class ScaleReading
{
    public int Id { get; set; }
    public double Weight { get; set; }
    public DateTime Timestamp { get; set; }
    public string? PalletId { get; set; }
}
