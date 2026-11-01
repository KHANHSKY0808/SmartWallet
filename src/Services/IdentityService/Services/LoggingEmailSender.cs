using System.Net.Mail;

namespace IdentityService.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = true);
}

public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;
    private readonly IConfiguration _configuration;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = true)
    {
        _logger.LogInformation("==================================================");
        _logger.LogInformation("📨 [EMAIL DISPATCH] To: {To} | Subject: {Subject}", to, subject);
        _logger.LogInformation("Body:\n{Body}", body);
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
                    Timeout = 3000
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress("no-reply@smartwallet.local", "SmartWallet System"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };
                mailMessage.To.Add(to);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation("✅ Email successfully forwarded to SMTP MailHog {Host}:{Port}", host, port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not forward email to MailHog SMTP ({Message}), logged to console fallback.", ex.Message);
        }
    }
}
