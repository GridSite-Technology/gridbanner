using System;

namespace GridBanner
{
    public enum AlertLevel
    {
        Routine,
        Urgent,
        Critical
    }

    public sealed record AlertMessage(
        string Signature,
        AlertLevel Level,
        string Summary,
        string Message,
        string BackgroundColor,
        string ForegroundColor
    );
}


