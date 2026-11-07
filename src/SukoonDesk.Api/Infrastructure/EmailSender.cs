using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SukoonDesk.Api.Infrastructure;

public sealed class EmailOptions
{
    public string Provider { get; set; } = "Smtp";
    public string ApiKey { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "SukoonDesk";
    public bool EnableSsl { get; set; } = true;
    public string PickupDirectory { get; set; } = string.Empty;
}

public interface IEmailSender
{
    bool IsConfigured { get; }
    string FromEmail { get; }
    Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken = default);
}

public sealed class EmailSender(IOptions<EmailOptions> options, IWebHostEnvironment environment, HttpClient httpClient) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;
    public string FromEmail => _options.FromEmail;
    public bool IsConfigured => (_options.Provider.Equals("Brevo", StringComparison.OrdinalIgnoreCase) || _options.Provider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
        ? !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.FromEmail)
        : !string.IsNullOrWhiteSpace(_options.PickupDirectory) ||
        (!string.IsNullOrWhiteSpace(_options.Host) && !string.IsNullOrWhiteSpace(_options.FromEmail));

    public async Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email delivery is not configured.");
        if (_options.Provider.Equals("Brevo", StringComparison.OrdinalIgnoreCase))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            request.Headers.Add("api-key", _options.ApiKey);
            request.Content = JsonContent.Create(new { sender = new { name = _options.FromName, email = _options.FromEmail }, to = new[] { new { email = to } }, subject, htmlContent = html });
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }
        if (_options.Provider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Content = JsonContent.Create(new { from = $"{_options.FromName} <{_options.FromEmail}>", to = new[] { to }, subject, html });
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }
        using var message = new MailMessage
        {
            From = new MailAddress(string.IsNullOrWhiteSpace(_options.FromEmail) ? "no-reply@sukoondesk.local" : _options.FromEmail, _options.FromName),
            Subject = subject,
            Body = html,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(to));

        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(_options.PickupDirectory))
        {
            var directory = Path.IsPathRooted(_options.PickupDirectory)
                ? _options.PickupDirectory
                : Path.Combine(environment.ContentRootPath, _options.PickupDirectory);
            Directory.CreateDirectory(directory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = directory;
        }
        else
        {
            client.Host = _options.Host;
            client.Port = _options.Port;
            client.EnableSsl = _options.EnableSsl;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            if (!string.IsNullOrWhiteSpace(_options.UserName))
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
