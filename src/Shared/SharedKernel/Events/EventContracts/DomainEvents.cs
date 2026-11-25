namespace SharedKernel.Events.EventContracts;

public record UserRegisteredEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string DefaultCurrency { get; init; } = "VND";
}

public record TransactionCreatedEvent : IntegrationEvent
{
    public Guid TransactionId { get; init; }
    public Guid UserId { get; init; }
    public Guid WalletId { get; init; }
    public Guid CategoryId { get; init; }
    public Guid? CategoryParentId { get; init; }
    public string? CategoryName { get; init; }
    public int Type { get; init; } // 1: Expense, 2: Income, 3: Transfer
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "VND";
    public DateTime TransactionDate { get; init; }
    public string? Note { get; init; }
}

public record TransactionUpdatedEvent : IntegrationEvent
{
    public Guid TransactionId { get; init; }
    public Guid UserId { get; init; }
    public Guid OldWalletId { get; init; }
    public Guid NewWalletId { get; init; }
    public Guid OldCategoryId { get; init; }
    public Guid NewCategoryId { get; init; }
    public Guid? OldCategoryParentId { get; init; }
    public Guid? NewCategoryParentId { get; init; }
    public string? CategoryName { get; init; }
    public int OldType { get; init; }
    public int NewType { get; init; }
    public decimal OldAmount { get; init; }
    public decimal NewAmount { get; init; }
    public string Currency { get; init; } = "VND";
    public DateTime TransactionDate { get; init; }
}

public record TransactionDeletedEvent : IntegrationEvent
{
    public Guid TransactionId { get; init; }
    public Guid UserId { get; init; }
    public Guid WalletId { get; init; }
    public Guid CategoryId { get; init; }
    public Guid? CategoryParentId { get; init; }
    public string? CategoryName { get; init; }
    public int Type { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "VND";
    public Guid? ReceiptFileId { get; init; }
}

public record BudgetThresholdReachedEvent : IntegrationEvent
{
    public Guid BudgetId { get; init; }
    public Guid UserId { get; init; }
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int Percent { get; init; } // 80 hoặc 100
    public decimal CurrentSpent { get; init; }
    public decimal BudgetLimit { get; init; }
    public int Month { get; init; }
    public int Year { get; init; }
}

public record MoneyTransferredEvent : IntegrationEvent
{
    public Guid TransferId { get; init; }
    public Guid UserId { get; init; }
    public Guid SourceWalletId { get; init; }
    public Guid TargetWalletId { get; init; }
    public Guid FromWalletId { get; init; }
    public Guid ToWalletId { get; init; }
    public decimal Amount { get; init; }
    public decimal Fee { get; init; }
    public string? Note { get; init; }
}

public record GoalFundsDepositedEvent : IntegrationEvent
{
    public Guid GoalId { get; init; }
    public Guid WalletId { get; init; }
    public Guid UserId { get; init; }
    public decimal Amount { get; init; }
    public string? GoalName { get; init; }
}

public record GoalFundsWithdrawnEvent : IntegrationEvent
{
    public Guid GoalId { get; init; }
    public Guid WalletId { get; init; }
    public Guid UserId { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public string? GoalName { get; init; }
}

public record GoalCompletedEvent : IntegrationEvent
{
    public Guid GoalId { get; init; }
    public Guid UserId { get; init; }
    public string GoalName { get; init; } = string.Empty;
    public decimal TargetAmount { get; init; }
    public decimal CurrentAmount { get; init; }
}

public record RecurringTransactionDueEvent : IntegrationEvent
{
    public Guid RuleId { get; init; }
    public Guid UserId { get; init; }
    public Guid WalletId { get; init; }
    public Guid CategoryId { get; init; }
    public int Type { get; init; } // 1: Expense, 2: Income
    public decimal Amount { get; init; }
    public DateOnly OccurrenceDate { get; init; }
    public string RuleName { get; init; } = string.Empty;
}

public record RecurringTransactionFailedEvent : IntegrationEvent
{
    public Guid RuleId { get; init; }
    public Guid UserId { get; init; }
    public DateOnly OccurrenceDate { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
}

public record DebtTransactionRequestedEvent : IntegrationEvent
{
    public Guid DebtId { get; init; }
    public Guid? PaymentId { get; init; }
    public Guid UserId { get; init; }
    public Guid WalletId { get; init; }
    public int Type { get; init; } // 1: Expense, 2: Income
    public decimal Amount { get; init; }
    public Guid CategoryId { get; init; }
    public string Note { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; }
}

public record DebtDeletedEvent : IntegrationEvent
{
    public Guid DebtId { get; init; }
    public Guid UserId { get; init; }
}

public record DueReminderEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public Guid SourceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ReminderType { get; init; } = "Reminder"; // "Recurring" | "Debt" | "Overdue"
    public DateOnly DueDate { get; init; }
    public decimal Amount { get; init; }
}

public record CategoryUpdatedEvent : IntegrationEvent
{
    public Guid CategoryId { get; init; }
    public Guid? UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
}

public record ExportCompletedEvent : IntegrationEvent
{
    public Guid JobId { get; init; }
    public Guid UserId { get; init; }
    public string Format { get; init; } = "xlsx"; // "xlsx" | "pdf"
}

/// <summary>D62: Yêu cầu gửi email bất đồng bộ từ IdentityService/services qua NotificationService.</summary>
public record EmailRequestedEvent : IntegrationEvent
{
    public Guid? UserId { get; init; }
    public string To { get; init; } = string.Empty;
    public string Template { get; init; } = string.Empty; // "ConfirmEmail" | "ResetPassword" | "DueReminder"
    public Dictionary<string, string> Data { get; init; } = new();
}

/// <summary>D63: Admin khóa tài khoản người dùng.</summary>
public record UserLockedEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public Guid ActorUserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>D63: Admin mở khóa tài khoản người dùng.</summary>
public record UserUnlockedEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public Guid ActorUserId { get; init; }
}

/// <summary>D63: Admin thay đổi vai trò của người dùng.</summary>
public record UserRolesChangedEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public Guid ActorUserId { get; init; }
    public List<string> Roles { get; init; } = new();
}



