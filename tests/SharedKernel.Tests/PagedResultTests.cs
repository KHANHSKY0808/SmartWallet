using SharedKernel.DTOs;
using Xunit;

namespace SharedKernel.Tests;

public class PagedResultTests
{
    [Fact]
    public void PagedResult_ShouldCalculateTotalPagesCorrectly()
    {
        var items = new List<string> { "Item 1", "Item 2" };
        var paged = new PagedResult<string>(items, totalCount: 25, pageNumber: 1, pageSize: 10);

        Assert.Equal(3, paged.TotalPages);
        Assert.False(paged.HasPreviousPage);
        Assert.True(paged.HasNextPage);
    }

    [Fact]
    public void PagedResult_MiddlePage_ShouldHaveBothPreviousAndNext()
    {
        var items = new List<string> { "Item 3" };
        var paged = new PagedResult<string>(items, totalCount: 30, pageNumber: 2, pageSize: 10);

        Assert.Equal(3, paged.TotalPages);
        Assert.True(paged.HasPreviousPage);
        Assert.True(paged.HasNextPage);
    }

    [Fact]
    public void PagedResult_LastPage_ShouldNotHaveNextPage()
    {
        var items = new List<string> { "Item 30" };
        var paged = new PagedResult<string>(items, totalCount: 30, pageNumber: 3, pageSize: 10);

        Assert.True(paged.HasPreviousPage);
        Assert.False(paged.HasNextPage);
    }
}

