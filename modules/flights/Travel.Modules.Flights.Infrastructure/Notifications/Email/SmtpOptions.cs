namespace Travel.Modules.Flights.Infrastructure.Notifications.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Flights:Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Travel Platform";
}
