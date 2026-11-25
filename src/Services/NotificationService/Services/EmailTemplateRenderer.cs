using System.Net;

namespace NotificationService.Services;

public static class EmailTemplateRenderer
{
    public static (string Subject, string BodyHtml) Render(string template, IDictionary<string, string>? rawData)
    {
        var data = rawData != null
            ? new Dictionary<string, string>(rawData, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string Get(string key) => data.TryGetValue(key, out var val) && val != null ? val : string.Empty;

        switch (template?.Trim())
        {
            case "ConfirmEmail":
            {
                var confirmLink = Get("confirmLink");
                var safeLink = WebUtility.HtmlEncode(confirmLink);
                var body = $@"
<h2>Chào mừng bạn đến với SmartWallet!</h2>
<p>Vui lòng nhấn vào liên kết bên dưới để xác nhận và kích hoạt tài khoản của bạn (liên kết có hiệu lực trong 24 giờ):</p>
<p><a href='{safeLink}'>Kích hoạt tài khoản SmartWallet</a></p>
<p>Hoặc truy cập trực tiếp URL: {confirmLink}</p>";
                return ("Chào mừng đến SmartWallet – xác nhận email", body);
            }
            case "ResetPassword":
            {
                var resetLink = Get("resetLink");
                var safeLink = WebUtility.HtmlEncode(resetLink);
                var body = $@"
<h2>Yêu cầu đặt lại mật khẩu</h2>
<p>Bạn vừa yêu cầu đặt lại mật khẩu cho tài khoản SmartWallet (liên kết có hiệu lực trong 30 phút):</p>
<p><a href='{safeLink}'>Đặt lại mật khẩu</a></p>
<p>Hoặc truy cập trực tiếp URL: {resetLink}</p>";
                return ("SmartWallet – Đặt lại mật khẩu", body);
            }
            case "DueReminder":
            {
                var title = Get("title");
                var message = Get("message");
                var subject = string.IsNullOrWhiteSpace(title) ? "SmartWallet – Nhắc hẹn đến hạn" : title;
                var body = $"<h3>{WebUtility.HtmlEncode(subject)}</h3><p>{WebUtility.HtmlEncode(message)}</p>";
                return (subject, body);
            }
            default:
            {
                var title = Get("title");
                var message = Get("message");
                var subject = string.IsNullOrWhiteSpace(title) ? "SmartWallet – Thông báo" : title;
                var body = $"<p>{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(message) ? template ?? string.Empty : message)}</p>";
                return (subject, body);
            }
        }
    }
}

