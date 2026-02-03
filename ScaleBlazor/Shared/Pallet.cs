namespace ScaleBlazor.Shared;

public class Pallet
{
    public int Id { get; set; }
    public string PalletId { get; set; } = string.Empty;
    public double TotalWeight { get; set; }
    public int ReadingCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCompleted { get; set; }
}
