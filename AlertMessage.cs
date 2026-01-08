using System;

namespace GridBanner
{
    public enum AlertLevel
    {
        Routine,
        Urgent,
        Critical,
        SuperCritical
    }

    public sealed record AlertMessage(
        string Signature,
        AlertLevel Level,
        string Summary,
        string Message,
        string BackgroundColor,
        string ForegroundColor,
        string? ContactName,
        string? ContactPhone,
        string? ContactEmail,
        string? ContactTeams,
        string? Site
    );
}


