namespace MyoTrack.Domain.Entities;

public class BodyMeasurement
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? HipCm { get; set; }
    public decimal? ArmCm { get; set; }
    public decimal? ThighCm { get; set; }
    public decimal? CalfCm { get; set; }
}
