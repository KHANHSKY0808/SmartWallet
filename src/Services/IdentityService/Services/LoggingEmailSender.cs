using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace IdentityService.Services;

public record EmailMessage(string To, string Subject, string Body, bool IsHtml = true);

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = true);
}

public class LoggingEmailSender : IEmailSender
{
    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(Channel<EmailMessage> channel, ILogger<LoggingEmailSender> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string body, bool isHtml = true)
    {
        _logger.LogInformation("📨 [EMAIL ENQUEUED FOR BACKGROUND SEND] To: {To} | Subject: {Subject}", to, subject);
        _channel.Writer.TryWrite(new EmailMessage(to, subject, body, isHtml));
        return Task.CompletedTask;
    }
}

public class EmailDispatchBackgroundService : BackgroundService
{
    private readonly Channel<EmailMessage> _channel;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailDispatchBackgroundService> _logger;

    public EmailDispatchBackgroundService(
        Channel<EmailMessage> channel,
        IConfiguration configuration,
        ILogger<EmailDispatchBackgroundService> logger)
    {
        _channel = channel;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested && await _channel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_channel.Reader.TryRead(out var msg))
            {
                await DispatchEmailAsync(msg, stoppingToken);
            }
        }
    }

    private async Task DispatchEmailAsync(EmailMessage msg, CancellationToken stoppingToken)
    {
        _logger.LogInformation("==================================================");
        _logger.LogInformation("📨 [EMAIL DISPATCH BACKGROUND] To: {To} | Subject: {Subject}", msg.To, msg.Subject);
        _logger.LogInformation("Body:\n{Body}", msg.Body);
        _logger.LogInformation("==================================================");

        try
        {
            var host = _configuration["Smtp:Host"] ?? "localhost";
            var portString = _configuration["Smtp:Port"] ?? "1025";
            if (int.TryParse(portString, out var port))
            {
                using var client = new SmtpClient(host, port)
                {
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    EnableSsl = false,
                    Timeout = 10000 // 10s (>= 5s theo N17)
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress("no-reply@smartwallet.local", "SmartWallet System"),
                    Subject = msg.Subject,
                    Body = msg.Body,
                    IsBodyHtml = msg.IsHtml
                };
                mailMessage.To.Add(msg.To);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                await client.SendMailAsync(mailMessage, cts.Token);
                _logger.LogInformation("✅ Email successfully forwarded to SMTP MailHog {Host}:{Port}", host, port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not forward email to MailHog SMTP ({Message}), logged to console fallback.", ex.Message);
        }
    }
}
