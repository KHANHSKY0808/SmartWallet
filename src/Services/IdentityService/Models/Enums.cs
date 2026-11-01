namespace IdentityService.Models;

public enum UserStatus
{
    PendingConfirm = 0,
    Active = 1,
    Locked = 2
}

public enum UserTokenType
{
    EmailConfirm = 1,
    PasswordReset = 2
}
