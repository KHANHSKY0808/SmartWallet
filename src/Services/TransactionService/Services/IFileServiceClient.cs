using System.Text.Json;

namespace TransactionService.Services;

public record FileValidationResult(bool Exists, Guid? OwnerUserId = null, int? Purpose = null);

public interface IFileServiceClient
{
    Task<FileValidationResult> ValidateFileAsync(Guid fileId, string? bearerToken = null, CancellationToken cancellationToken = default);
}

public class FileServiceClient : IFileServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FileServiceClient> _logger;

    public FileServiceClient(HttpClient httpClient, ILogger<FileServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FileValidationResult> ValidateFileAsync(Guid fileId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/files/{fileId}");
            // D65: Endpoint /internal/* yêu cầu System token (gắn tự động qua SystemTokenDelegatingHandler)

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new FileValidationResult(false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            
            Guid? ownerId = null;
            if (doc.RootElement.TryGetProperty("ownerUserId", out var ownerProp) && ownerProp.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(ownerProp.GetString(), out var parsedOwnerId))
                {
                    ownerId = parsedOwnerId;
                }
            }

            int? purpose = null;
            if (doc.RootElement.TryGetProperty("purpose", out var purposeProp))
            {
                purpose = purposeProp.GetInt32();
            }

            return new FileValidationResult(true, ownerId, purpose);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gọi FileService để xác thực file {FileId}", fileId);
            return new FileValidationResult(false);
        }
    }
}
