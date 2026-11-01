using SharedKernel.DTOs;
using Xunit;

namespace SharedKernel.Tests;

public class ApiResponseTests
{
    [Fact]
    public void ApiResponse_Ok_ShouldReturnSuccessTrue()
    {
        var response = ApiResponse.Ok("Thao tác thành công");

        Assert.True(response.Success);
        Assert.Equal("Thao tác thành công", response.Message);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void ApiResponse_Fail_ShouldReturnSuccessFalseWithErrors()
    {
        var errors = new[] { "Lỗi 1", "Lỗi 2" };
        var response = ApiResponse.Fail("Có lỗi xảy ra", errors);

        Assert.False(response.Success);
        Assert.Equal("Có lỗi xảy ra", response.Message);
        Assert.Equal(2, response.Errors.Count());
    }

    [Fact]
    public void ApiResponseGeneric_Ok_ShouldContainData()
    {
        var data = new { UserId = Guid.NewGuid(), Email = "test@example.com" };
        var response = ApiResponse<object>.Ok(data, "Lấy thông tin thành công");

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(data, response.Data);
    }
}

