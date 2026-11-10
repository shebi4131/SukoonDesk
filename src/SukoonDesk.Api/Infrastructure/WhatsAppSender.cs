using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SukoonDesk.Api.Domain;

namespace SukoonDesk.Api.Infrastructure;

public sealed class WhatsAppOptions
{
    public string Provider { get; set; } = "Disabled";
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v21.0";
}

public interface IWhatsAppSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string message, CancellationToken cancellationToken = default);
}

public sealed class WhatsAppSender(IOptions<WhatsAppOptions> options, HttpClient httpClient) : IWhatsAppSender
{
    private readonly WhatsAppOptions _options = options.Value;
    public bool IsConfigured => _options.Provider.Equals("Meta", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(_options.AccessToken) && !string.IsNullOrWhiteSpace(_options.PhoneNumberId);

    public async Task SendAsync(string recipient, string message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return;
        var number = NormalizePakistaniNumber(recipient);
        if (string.IsNullOrWhiteSpace(number)) return;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{_options.ApiVersion}/{_options.PhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = number,
            type = "text",
            text = new { preview_url = false, body = message }
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    internal static string NormalizePakistaniNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.StartsWith('0')) digits = "92" + digits[1..];
        if (digits.Length == 10 && digits.StartsWith('3')) digits = "92" + digits;
        return digits;
    }
}

public sealed class NotificationDispatcher(
    AppDbContext db,
    IEmailSender emailSender,
    IWhatsAppSender whatsAppSender,
    ILogger<NotificationDispatcher> logger)
{
    public async Task NotifyAsync(Guid tenantId, Guid membershipId, string title, string message, string type, string link,
        bool sendEmail, bool sendWhatsApp, CancellationToken cancellationToken = default)
    {
        db.Notifications.Add(new Notification
        {
            TenantId = tenantId, MembershipId = membershipId, Title = title, Message = message, Type = type, Link = link
        });

        var recipient = await db.TenantMemberships.AsNoTracking().Where(x => x.Id == membershipId && x.TenantId == tenantId)
            .Select(x => new { x.User.Email, x.User.PhoneNumber, x.User.DisplayName }).SingleOrDefaultAsync(cancellationToken);
        if (recipient is null) return;
        var resident = await db.PublicMembers.AsNoTracking().SingleOrDefaultAsync(x => x.MembershipId == membershipId, cancellationToken);
        var email = resident?.Email ?? recipient.Email;
        var whatsapp = resident?.WhatsAppNumber ?? recipient.PhoneNumber;
        sendEmail = sendEmail && (resident?.NotifyByEmail ?? true);
        sendWhatsApp = sendWhatsApp && (resident?.NotifyByWhatsApp ?? true);

        if (sendEmail && emailSender.IsConfigured && !string.IsNullOrWhiteSpace(email))
        {
            try
            {
                var safeTitle = System.Net.WebUtility.HtmlEncode(title);
                var safeMessage = System.Net.WebUtility.HtmlEncode(message);
                await emailSender.SendAsync(email, $"SukoonDesk — {title}",
                    $"<div style=\"font-family:Arial,sans-serif;max-width:580px;margin:auto;color:#0f2f3a\"><div style=\"background:#155e75;color:white;padding:24px 28px;border-radius:16px 16px 0 0\"><h1 style=\"margin:0\">SukoonDesk</h1></div><div style=\"border:1px solid #dbe5e8;border-top:0;padding:28px;border-radius:0 0 16px 16px\"><h2>{safeTitle}</h2><p style=\"line-height:1.6\">{safeMessage}</p></div></div>", cancellationToken);
            }
            catch (Exception exception) { logger.LogWarning(exception, "Email notification delivery failed for membership {MembershipId}", membershipId); }
        }

        if (sendWhatsApp && whatsAppSender.IsConfigured && !string.IsNullOrWhiteSpace(whatsapp))
        {
            try { await whatsAppSender.SendAsync(whatsapp, $"SukoonDesk\n{title}\n{message}", cancellationToken); }
            catch (Exception exception) { logger.LogWarning(exception, "WhatsApp notification delivery failed for membership {MembershipId}", membershipId); }
        }
    }
}
