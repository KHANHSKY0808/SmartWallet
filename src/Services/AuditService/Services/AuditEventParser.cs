using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AuditService.Models;

namespace AuditService.Services;

public static partial class AuditEventParser
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "password", "token", "secret", "otp", "code"
    ];

    private static readonly string[] EntityIdCandidateKeys =
    [
        "TransactionId", "WalletId", "CategoryId", "BudgetId", "GoalId", "RuleId", "DebtId", "JobId", "FileId", "EntityId", "UserId"
    ];

    [GeneratedRegex(@"\b([\w.+-]{1,})@([\w-]+\.[\w.]+)\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    public static string ResolveServiceName(string? routingKey)
    {
        if (string.IsNullOrWhiteSpace(routingKey)) return "Unknown";
        var prefix = routingKey.Split('.', 2)[0].Trim().ToLowerInvariant();
        return prefix switch
        {
            "user" or "identity" => "IdentityService",
            "profile" => "UserProfileService",
            "wallet" => "WalletService",
            "transaction" => "TransactionService",
            "category" => "CategoryService",
            "budget" => "BudgetService",
            "goal" or "saving" => "SavingGoalService",
            "recurring" or "debt" or "reminder" => "RecurringDebtService",
            "report" => "ReportService",
            "notification" => "NotificationService",
            "file" => "FileService",
            _ => "Unknown"
        };
    }

    public static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;
        var atIdx = email.IndexOf('@');
        if (atIdx <= 0 || atIdx >= email.Length - 1) return "***";
        return $"{email[0]}***{email[atIdx..]}";
    }

    public static AuditLog ParseAndMask(string rawJson, string? routingKey, string? messageId, string? correlationId, string? headerEventType)
    {
        JsonNode? rootNode = null;
        try
        {
            rootNode = JsonNode.Parse(rawJson);
        }
        catch
        {
            // Giữ raw an toàn nếu payload không phải JSON chuẩn
        }

        Guid? eventId = TryParseGuid(messageId);
        Guid? userId = null;
        Guid? actorUserId = null;
        string action = !string.IsNullOrWhiteSpace(headerEventType) ? headerEventType : (routingKey ?? "UnknownEvent");
        string? entityId = null;
        string? entityType = null;
        var timestamp = DateTime.UtcNow;

        if (rootNode is JsonObject obj)
        {
            eventId ??= GetGuidProperty(obj, "Id") ?? GetGuidProperty(obj, "EventId");
            if (TryGetStringProperty(obj, "EventType", out var evType) && !string.IsNullOrWhiteSpace(evType))
            {
                action = evType!;
            }

            if (TryGetStringProperty(obj, "OccurredAt", out var occ) && DateTime.TryParse(occ, out var parsedTs))
            {
                timestamp = DateTime.SpecifyKind(parsedTs.ToUniversalTime(), DateTimeKind.Utc);
            }

            actorUserId = GetGuidProperty(obj, "ActorUserId");
            var payloadUserId = GetGuidProperty(obj, "UserId");
            // D63: Thao tác Admin ưu tiên ghi UserId là người thực hiện (ActorUserId), EntityId là User bị tác động
            userId = actorUserId ?? payloadUserId;

            if (actorUserId.HasValue && payloadUserId.HasValue)
            {
                entityId = payloadUserId.Value.ToString();
                entityType = "User";
            }
            else
            {
                foreach (var candidate in EntityIdCandidateKeys)
                {
                    if (TryGetStringProperty(obj, candidate, out var val) && !string.IsNullOrWhiteSpace(val))
                    {
                        entityId = val;
                        entityType = candidate.EndsWith("Id", StringComparison.Ordinal)
                            ? candidate[..^2]
                            : candidate;
                        break;
                    }
                }
            }

            // D62/D63: EmailRequested chỉ giữ To (đã che) và Template, tuyệt đối không lưu Data (chứa token)
            if (string.Equals(action, "EmailRequestedEvent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routingKey, "identity.email_requested", StringComparison.OrdinalIgnoreCase))
            {
                var keysToRemove = obj.Select(kv => kv.Key)
                    .Where(k => !string.Equals(k, "to", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "template", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "id", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "occurredAt", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "eventType", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "userId", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var k in keysToRemove)
                {
                    obj.Remove(k);
                }
            }

            MaskNode(obj);
        }

        var maskedJson = rootNode?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        return new AuditLog
        {
            EventId = eventId,
            UserId = userId,
            ServiceName = ResolveServiceName(routingKey),
            Action = action.Length > 100 ? action[..100] : action,
            EntityType = entityType,
            EntityId = entityId,
            NewValueJson = maskedJson,
            CorrelationId = correlationId ?? eventId?.ToString(),
            Timestamp = timestamp
        };
    }

    private static void MaskNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kv => kv.Key).ToList();
            foreach (var key in keys)
            {
                if (IsSensitiveKey(key))
                {
                    obj.Remove(key);
                    continue;
                }

                var child = obj[key];
                if (child is JsonValue val && val.TryGetValue<string>(out var strVal))
                {
                    if (key.Contains("email", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "to", StringComparison.OrdinalIgnoreCase))
                    {
                        obj[key] = MaskEmail(strVal);
                    }
                    else
                    {
                        obj[key] = MaskEmbeddedEmails(strVal);
                    }
                }
                else
                {
                    MaskNode(child);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is JsonValue val && val.TryGetValue<string>(out var s))
                {
                    arr[i] = MaskEmbeddedEmails(s);
                }
                else
                {
                    MaskNode(arr[i]);
                }
            }
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        foreach (var fragment in SensitiveKeyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string MaskEmbeddedEmails(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains('@')) return input;
        return EmailRegex().Replace(input, m => MaskEmail(m.Value));
    }

    private static Guid? GetGuidProperty(JsonObject obj, string name)
    {
        return TryGetStringProperty(obj, name, out var val) ? TryParseGuid(val) : null;
    }

    private static bool TryGetStringProperty(JsonObject obj, string name, out string? value)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && kv.Value is JsonValue v)
            {
                value = v.ToString();
                return true;
            }
        }
        value = null;
        return false;
    }

    private static Guid? TryParseGuid(string? raw)
        => Guid.TryParse(raw, out var g) && g != Guid.Empty ? g : null;
}

