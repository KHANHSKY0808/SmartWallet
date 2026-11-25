using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;

namespace NotificationService.Services;

public interface ISmtpEmailClient
{
    Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken cancellationToken = default);
}

public sealed class MailKitSmtpEmailClient : ISmtpEmailClient
{
    private readonly IConfiguration _configuration;

    public MailKitSmtpEmailClient(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"] ?? "localhost";
        var port = int.TryParse(_configuration["Smtp:Port"], out var p) ? p : 1025;
        var from = _configuration["Smtp:From"] ?? "no-reply@smartwallet.local";

        using var client = new SmtpClient(host, port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            EnableSsl = false,
            Timeout = 3000
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(from, "SmartWallet System"),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = bodyHtml,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = TransferEncoding.QuotedPrintable,
            IsBodyHtml = true
        };
        mail.To.Add(toEmail);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        await client.SendMailAsync(mail, cts.Token);
    }
}

public interface IEmailDispatchSignal
{
    void NotifyNewEmail();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class EmailDispatchSignal : IEmailDispatchSignal
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    public void NotifyNewEmail() => _signal.Release();

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _signal.WaitAsync(timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class EmailDispatcherService
{
    private readonly NotificationDbContext _context;
    private readonly ISmtpEmailClient _smtpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailDispatcherService> _logger;
    private readonly int[] _retryDelaysSeconds;

    public EmailDispatcherService(
        NotificationDbContext context,
        ISmtpEmailClient smtpClient,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EmailDispatcherService> logger)
    {
        _context = context;
        _smtpClient = smtpClient;
        _timeProvider = timeProvider;
        _logger = logger;

        var configured = configuration.GetSection("Email:RetryDelaysSeconds").Get<int[]>();
        _retryDelaysSeconds = configured is { Length: > 0 }
            ? configured
            : [60, 300, 900];
    }

    public async Task<int> ProcessDueEmailsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var dueItems = await _context.EmailQueue
            .Where(e => e.Status == EmailQueueStatus.Pending && (e.NextRetryAt == null || e.NextRetryAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        int sentCount = 0;

        foreach (var item in dueItems)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var body = item.BodyHtml ?? string.Empty;
            item.Attempts++;
            item.UpdatedAt = now;

            try
            {
                await _smtpClient.SendAsync(item.ToEmail, item.Subject, body, cancellationToken);
                item.Status = EmailQueueStatus.Sent;
                item.SentAt = _timeProvider.GetUtcNow().UtcDateTime;
                item.NextRetryAt = null;
                item.LastError = null;
                sentCount++;
                // D62/EM-06: Tuyệt đối không log body hay token
                _logger.LogInformation("Đã gửi email hàng đợi {EmailId} (Template: {Template}, Attempts: {Attempts})",
                    item.Id, item.TemplateCode, item.Attempts);
            }
            catch (Exception ex)
            {
                var currentNow = _timeProvider.GetUtcNow().UtcDateTime;
                item.LastError = ex.Message.Length > 950 ? ex.Message[..950] : ex.Message;

                var delayIndex = item.Attempts - 1;
                if (delayIndex < _retryDelaysSeconds.Length)
                {
                    item.Status = EmailQueueStatus.Pending;
                    item.NextRetryAt = currentNow.AddSeconds(_retryDelaysSeconds[delayIndex]);
                    _logger.LogWarning("Gửi email {EmailId} thất bại (Lần {Attempts}). Lên lịch thử lại sau {Delay}s.",
                        item.Id, item.Attempts, _retryDelaysSeconds[delayIndex]);
                }
                else
                {
                    item.Status = EmailQueueStatus.Failed;
                    item.NextRetryAt = null;
                    _logger.LogError("Gửi email {EmailId} thất bại vĩnh viễn sau {Attempts} lần thử.",
                        item.Id, item.Attempts);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        await CleanupExpiredBodiesAsync(cancellationToken);
        return sentCount;
    }

    public async Task<int> CleanupExpiredBodiesAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-24);
        var expired = await _context.EmailQueue
            .Where(e => e.Status == EmailQueueStatus.Sent && e.SentAt != null && e.SentAt <= cutoff && e.BodyHtml != null)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return 0;

        foreach (var item in expired)
        {
            item.BodyHtml = null;
            item.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã xóa nội dung BodyHtml cho {Count} email đã gửi quá 24 giờ (D62).", expired.Count);
        return expired.Count;
    }
}

public sealed class EmailDispatchWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEmailDispatchSignal _signal;
    private readonly ILogger<EmailDispatchWorker> _logger;

    public EmailDispatchWorker(
        IServiceProvider serviceProvider,
        IEmailDispatchSignal signal,
        ILogger<EmailDispatchWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<EmailDispatcherService>();
                await dispatcher.ProcessDueEmailsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi vòng lặp worker gửi email.");
            }

            await _signal.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

