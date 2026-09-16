using System.Net;
using System.Net.Mail;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Sends alert notification emails over SMTP. Configured under <c>Email:Smtp</c>
/// (Host, Port, EnableSsl, Username, Password), <c>Email:From</c> (Address, Name)
/// and <c>Email:AlertRecipients</c> (array of addresses). Mirrors
/// <see cref="LocalAlertPushClient"/>: when the SMTP host or recipients are not
/// configured, the send is skipped rather than failing the alert flow, and any
/// send error is logged and swallowed.
/// </summary>
public class AlertEmailService : IAlertEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlertEmailService> _logger;

    public AlertEmailService(IConfiguration configuration, ILogger<AlertEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAlertCreatedNotificationAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        var smtpSection = _configuration.GetSection("Email:Smtp");
        var host = smtpSection["Host"];

        var recipients = _configuration.GetSection("Email:AlertRecipients").Get<string[]>() ?? [];

        if (string.IsNullOrWhiteSpace(host) || recipients.Length == 0)
        {
            _logger.LogWarning(
                "Email:Smtp:Host or Email:AlertRecipients is not configured. Skipping alert notification email for alert {AlertId}.",
                alert.Id);
            return;
        }

        var port = smtpSection.GetValue<int?>("Port") ?? 587;
        var enableSsl = smtpSection.GetValue<bool?>("EnableSsl") ?? true;
        var username = smtpSection["Username"];
        var password = smtpSection["Password"];

        var fromAddress = _configuration["Email:From:Address"] ?? "alerts@futurekawa.com";
        var fromName = _configuration["Email:From:Name"] ?? "FutureKawa Alerts";

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = BuildSubject(alert),
                Body = BuildBody(alert),
                IsBodyHtml = true,
            };

            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Timeout = 50000,
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation(
                "Sent alert notification email for alert {AlertId} to {RecipientCount} recipient(s).",
                alert.Id, recipients.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send alert notification email for alert {AlertId} via {Host}:{Port}.",
                alert.Id, host, port);
        }
    }

    private static string BuildSubject(Alert alert) =>
        $"[FutureKawa] {alert.Type} alert - {alert.Warehouse.Name} ({alert.Warehouse.Country.Code})";

    private static string BuildBody(Alert alert)
    {
        var warehouse = alert.Warehouse;
        var country = warehouse.Country;

        var measuredAt = alert.MeasuredAt.HasValue
            ? $"{alert.MeasuredAt:yyyy-MM-dd HH:mm} UTC"
            : "n/a";

        return $"""
            <h2>New {alert.Type} alert</h2>
            <table>
              <tr><td><strong>Alert type</strong></td><td>{alert.Type}</td></tr>
              <tr><td><strong>Country</strong></td><td>{country.Name} ({country.Code})</td></tr>
              <tr><td><strong>Warehouse</strong></td><td>{warehouse.Name} ({warehouse.Reference})</td></tr>
              <tr><td><strong>Measured at</strong></td><td>{measuredAt}</td></tr>
              <tr><td><strong>Received at</strong></td><td>{alert.CreatedAt:yyyy-MM-dd HH:mm} UTC</td></tr>
              <tr><td><strong>Alert ID</strong></td><td>{alert.Id}</td></tr>
            </table>
            """;
    }
}
