using System.Collections.Concurrent;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MuslimMatrimony.Services;

public sealed class EmailOptions
{
    /// <summary>SMTP host. When empty, mail is not sent: it is logged and kept in <see cref="EmailOutbox"/>.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    /// <summary>Supply via environment/secret store (Email__Password), never a committed file.</summary>
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@localhost";
    public string FromName { get; set; } = "Nikah Noor";
    /// <summary>Auto, StartTls or SslOnConnect.</summary>
    public string Security { get; set; } = "StartTls";
}

public sealed record SentEmail(string To, string Subject, string HtmlBody, DateTime SentAt);

/// <summary>Keeps the last messages when SMTP is not configured (development and tests).</summary>
public sealed class EmailOutbox
{
    private readonly ConcurrentQueue<SentEmail> _messages = new();

    public void Add(SentEmail email)
    {
        _messages.Enqueue(email);
        while (_messages.Count > 200 && _messages.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<SentEmail> All => _messages.ToArray();

    public SentEmail? LastTo(string to) => _messages.Reverse().FirstOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, EmailOutbox outbox, IHostEnvironment env, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            outbox.Add(new SentEmail(email, subject, htmlMessage, DateTime.UtcNow));
            // Bodies contain account links; only print them in Development.
            if (env.IsDevelopment())
            {
                logger.LogInformation("SMTP not configured. Email to {To}: {Subject}\n{Body}", email, subject, htmlMessage);
            }
            else
            {
                logger.LogWarning("SMTP not configured; email to {To} ({Subject}) was not sent", email, subject);
            }

            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlMessage }.ToMessageBody();

        using var client = new SmtpClient();
        var security = Enum.TryParse<SecureSocketOptions>(o.Security, true, out var s) ? s : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.Host, o.Port, security);
        if (!string.IsNullOrEmpty(o.Username))
        {
            await client.AuthenticateAsync(o.Username, o.Password ?? string.Empty);
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
