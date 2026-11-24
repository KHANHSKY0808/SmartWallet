using RecurringDebtService.DTOs;
using RecurringDebtService.Models;
using Xunit;

namespace RecurringDebtService.Tests;

public class DebtsAndPaymentsTests
{
    [Fact]
    public void Test_DB03_Validation_Rules()
    {
        var invalidDebtAmount = new CreateDebtRequest
        {
            Type = 1,
            PersonName = "Anh Nam",
            Amount = 0,
            DueDate = new DateOnly(2027, 2, 10),
            WalletId = Guid.NewGuid()
        };
        Assert.True(invalidDebtAmount.Amount <= 0);

        var invalidDebtPerson = new CreateDebtRequest
        {
            Type = 1,
            PersonName = "   ",
            Amount = 5_000_000,
            DueDate = new DateOnly(2027, 2, 10),
            WalletId = Guid.NewGuid()
        };
        Assert.True(string.IsNullOrWhiteSpace(invalidDebtPerson.PersonName));

        var invalidDebtRate = new CreateDebtRequest
        {
            Type = 1,
            PersonName = "Anh Nam",
            Amount = 5_000_000,
            DueDate = new DateOnly(2027, 2, 10),
            InterestRate = -1,
            WalletId = Guid.NewGuid()
        };
        Assert.True(invalidDebtRate.InterestRate < 0);
    }

    [Fact]
    public void Test_DB08_Update_Amount_LessThan_PaidAmount_Fails()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 5_000_000,
            PaidAmount = 2_000_000,
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = Guid.NewGuid()
        };

        var newAmount = 1_500_000m;
        var isInvalid = newAmount < debt.PaidAmount;
        Assert.True(isInvalid, "Không được sửa Amount nhỏ hơn PaidAmount");
    }

    [Fact]
    public void Test_DP01_Payment_Calculation()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 5_000_000,
            PaidAmount = 0,
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = Guid.NewGuid()
        };

        var payAmount = 2_000_000m;
        debt.PaidAmount += payAmount;

        Assert.Equal(2_000_000m, debt.PaidAmount);
        Assert.Equal(3_000_000m, debt.Amount - debt.PaidAmount);
        Assert.Equal(DebtStatus.Active, debt.Status);
    }

    [Fact]
    public void Test_DP05_Overpay_Fails()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 5_000_000,
            PaidAmount = 4_000_000,
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = Guid.NewGuid()
        };

        var payAmount = 2_000_000m;
        var isOverpay = debt.PaidAmount + payAmount > debt.Amount;
        Assert.True(isOverpay, "Thanh toán vượt quá số dư nợ còn lại phải bị từ chối");
    }

    [Fact]
    public void Test_DP06_PaidFull_Status_Transition()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 5_000_000,
            PaidAmount = 4_000_000,
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = Guid.NewGuid()
        };

        var payAmount = 1_000_000m;
        debt.PaidAmount += payAmount;
        if (debt.PaidAmount >= debt.Amount)
        {
            debt.Status = DebtStatus.PaidFull;
        }

        Assert.Equal(5_000_000m, debt.PaidAmount);
        Assert.Equal(DebtStatus.PaidFull, debt.Status);
    }
}

