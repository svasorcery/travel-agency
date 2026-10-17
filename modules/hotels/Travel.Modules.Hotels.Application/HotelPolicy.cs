namespace Travel.Modules.Hotels.Application;

// Local policies only. Captured deadlines/allowances on a reservation never follow later configuration changes.
public sealed class HotelPolicy
{
    public int SnapshotSeconds { get; set; } = 300;
    public int QuoteSeconds { get; set; } = 300;
    public int EscalationSeconds { get; set; } = 600;
    public int ReadWindowSeconds { get; set; } = 300;
    public int ReadAllowance { get; set; } = 3;
    public int SecondReadDelaySeconds { get; set; } = 30;
    public int ThirdReadDelaySeconds { get; set; } = 120;
    public int DraftRetentionHours { get; set; } = 24;
    public int FinishedRetentionDays { get; set; } = 90;

    public bool IsValid() =>
        SnapshotSeconds is >= 1 and <= 300
        && QuoteSeconds is >= 1 and <= 300
        && EscalationSeconds is >= 1 and <= 3600
        && ReadWindowSeconds is >= 1 and <= 300
        && ReadAllowance is >= 1 and <= 3
        && SecondReadDelaySeconds is >= 1 and <= 120
        && ThirdReadDelaySeconds is >= 1 and <= 120
        && DraftRetentionHours is >= 1 and <= 168
        && FinishedRetentionDays is >= 1 and <= 365;
}
