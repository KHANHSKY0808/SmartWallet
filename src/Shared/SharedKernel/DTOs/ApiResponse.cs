using System.Text.Json.Serialization;

namespace SharedKernel.DTOs;

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public IEnumerable<string> Errors { get; set; } = Enumerable.Empty<string>();

    public static ApiResponse Ok(string message = "Thành công") => new()
    {
        Success = true,
        Message = message
    };

    public static ApiResponse Fail(string message, IEnumerable<string>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors ?? Enumerable.Empty<string>()
    };

    public static ApiResponse Fail(string message, string error) => new()
    {
        Success = false,
        Message = message,
        Errors = new[] { error }
    };
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Thành công") => new()
    {
        Success = true,
        Message = message,
        Data = data
    };

    public new static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors ?? Enumerable.Empty<string>()
    };

    public new static ApiResponse<T> Fail(string message, string error) => new()
    {
        Success = false,
        Message = message,
        Errors = new[] { error }
    };
}

