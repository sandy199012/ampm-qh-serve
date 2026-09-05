using AMPMWeb.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AMPMWeb.Services;

// Sends real HTML emails (e.g. helpdesk ticket notifications) via SMTP — Office 365 / Outlook by default.
// Config priority: environment variables (SMTP_HOST/SMTP_PORT/SMTP_USER/SMTP_PASS/SMTP_FROM/SMTP_FROM_NAME)
// first, then falls back to values saved via the app's Email Settings screen (stored in the kv table).
public class EmailService
{
    private readonly DbService _db;
    public EmailService(DbService db) { _db = db; }

    Dictionary<string,object?> Settings()
        => _db.KGetObj<Dictionary<string,object?>>("smtp_settings") ?? new();

    string Get(string envVar, string settingsKey, string fallback = "")
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var s = Settings();
        var v = s.GetValueOrDefault(settingsKey)?.ToString();
        return !string.IsNullOrWhiteSpace(v) ? v : fallback;
    }

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(Get("SMTP_USER", "smtpUser")) && !string.IsNullOrWhiteSpace(Get("SMTP_PASS", "smtpPass"));

    public async Task<(bool ok, string? error)> SendAsync(string to, string? cc, string subject, string htmlBody)
    {
        try
        {
            var host = Get("SMTP_HOST", "smtpHost", "smtp.office365.com");
            var port = int.TryParse(Get("SMTP_PORT", "smtpPort", "587"), out var p) ? p : 587;
            var user = Get("SMTP_USER", "smtpUser");
            var pass = Get("SMTP_PASS", "smtpPass");
            var fromEmail = Get("SMTP_FROM", "smtpFrom", user);
            var fromName = Get("SMTP_FROM_NAME", "smtpFromName", "AMPM IT Helpdesk");

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                return (false, "SMTP not configured yet. Set it up on the Email Settings page (or as SMTP_HOST / SMTP_USER / SMTP_PASS environment variables in Render).");
            if (string.IsNullOrWhiteSpace(to))
                return (false, "Employee email address is missing for this ticket.");

            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(fromName, fromEmail));
            msg.To.Add(MailboxAddress.Parse(to));
            if (!string.IsNullOrWhiteSpace(cc))
                foreach (var c in cc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    msg.Cc.Add(MailboxAddress.Parse(c));
            msg.Subject = subject;
            msg.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(user, pass);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
