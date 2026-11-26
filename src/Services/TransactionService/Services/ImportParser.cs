using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TransactionService.Models;

namespace TransactionService.Services;

public class ImportValidationException : Exception
{
    public ImportValidationException(string message) : base(message) { }
}

public record ImportColumnMapping(
    string? Date,
    string? Amount,
    string? Type,
    string? Category,
    string? Wallet,
    string? Note);

public record ImportRawRow(
    int RowNumber,
    string DateRaw,
    string AmountRaw,
    string TypeRaw,
    string CategoryRaw,
    string WalletRaw,
    string NoteRaw);

public record ParsedImportRow(
    DateTime TransactionDateUtc,
    decimal Amount,
    TransactionType Type,
    Guid WalletId,
    string WalletCurrency,
    Guid CategoryId,
    Guid? CategoryParentId,
    string CategoryName,
    string? Note);

public static partial class ImportParser
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;
    public const int MaxDataRows = 10_000;

    [GeneratedRegex(@"^(?:\d+|\d{1,3}(?:[.\s]\d{3})+)$")]
    private static partial Regex ValidIntAmountRegex();

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static Guid DeterministicRowGuid(Guid jobId, int rowNumber)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{jobId:N}:{rowNumber}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public static string SanitizeCsvCell(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (input[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            return "'" + input;
        }

        return input;
    }

    public static string EscapeCsvCell(string? value)
    {
        var safe = SanitizeCsvCell(value);
        if (safe.Contains(',') || safe.Contains('"') || safe.Contains('\n') || safe.Contains('\r'))
        {
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
        return safe;
    }

    public static List<ImportRawRow> ParseFile(byte[] fileBytes, string extension, string? columnMappingJson = null)
    {
        if (fileBytes.Length == 0)
        {
            throw new ImportValidationException("Tệp tải lên không được rỗng.");
        }

        if (fileBytes.Length > MaxFileSizeBytes)
        {
            throw new ImportValidationException("Kích thước tệp vượt quá giới hạn tối đa 5 MB.");
        }

        ImportColumnMapping? mapping = null;
        if (!string.IsNullOrWhiteSpace(columnMappingJson))
        {
            try
            {
                mapping = JsonSerializer.Deserialize<ImportColumnMapping>(columnMappingJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                throw new ImportValidationException("Cấu hình ánh xạ cột không hợp lệ.");
            }
        }

        List<List<string>> table = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadXlsxTable(fileBytes)
            : ReadCsvTable(fileBytes);

        if (table.Count < 2)
        {
            throw new ImportValidationException("Tệp phải chứa dòng tiêu đề và ít nhất 1 dòng dữ liệu.");
        }

        var headers = table[0];
        var (idxDate, idxAmount, idxType, idxCat, idxWallet, idxNote) = ResolveColumns(headers, mapping);

        var rows = new List<ImportRawRow>();
        for (int i = 1; i < table.Count; i++)
        {
            var cols = table[i];
            if (cols.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(new ImportRawRow(
                RowNumber: i, // 1-based data row index
                DateRaw: GetCell(cols, idxDate),
                AmountRaw: GetCell(cols, idxAmount),
                TypeRaw: GetCell(cols, idxType),
                CategoryRaw: GetCell(cols, idxCat),
                WalletRaw: GetCell(cols, idxWallet),
                NoteRaw: idxNote >= 0 ? GetCell(cols, idxNote) : string.Empty));

            if (rows.Count > MaxDataRows)
            {
                throw new ImportValidationException($"Số dòng dữ liệu vượt quá giới hạn tối đa {MaxDataRows:N0} dòng.");
            }
        }

        if (rows.Count == 0)
        {
            throw new ImportValidationException("Tệp không có dòng dữ liệu hợp lệ nào.");
        }

        return rows;
    }

    public static bool TryValidateAndResolveRow(
        ImportRawRow row,
        IReadOnlyDictionary<string, ImportWalletInfo> walletsByName,
        ILookup<string, ImportCategoryInfo> categoriesByName,
        out ParsedImportRow? parsed,
        out string errorReason)
    {
        parsed = null;

        // 1. Ngay: dd/MM/yyyy hoặc yyyy-MM-dd, hiểu theo giờ VN (D31, D68)
        var dateText = row.DateRaw.Trim();
        if (!DateOnly.TryParseExact(dateText, ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            errorReason = $"Ngày không hợp lệ ('{dateText}'). Cần định dạng dd/MM/yyyy hoặc yyyy-MM-dd.";
            return false;
        }
        var txDateUtc = new DateTimeOffset(parsedDate.Year, parsedDate.Month, parsedDate.Day, 0, 0, 0, VietnamOffset).UtcDateTime;

        // 2. Loai: Chi/Thu hoặc 1/2. Type 3 bị từ chối (D68)
        var typeNorm = NormalizeKey(row.TypeRaw);
        TransactionType txType;
        if (typeNorm is "chi" or "expense" or "1")
        {
            txType = TransactionType.Expense;
        }
        else if (typeNorm is "thu" or "income" or "2")
        {
            txType = TransactionType.Income;
        }
        else
        {
            errorReason = $"Loại giao dịch không hợp lệ ('{row.TypeRaw.Trim()}'). Chỉ chấp nhận Chi/Thu hoặc 1/2.";
            return false;
        }

        // 3. SoTien: số nguyên dương, chấp nhận '.' hoặc khoảng trắng ngăn nghìn, từ chối âm/0/thập phân
        var amountText = row.AmountRaw.Trim();
        if (string.IsNullOrWhiteSpace(amountText) || !ValidIntAmountRegex().IsMatch(amountText))
        {
            errorReason = $"Số tiền không hợp lệ ('{amountText}'). Yêu cầu số nguyên dương.";
            return false;
        }

        var digitsOnly = amountText.Replace(".", string.Empty).Replace(" ", string.Empty);
        if (!decimal.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            errorReason = $"Số tiền phải lớn hơn 0 ('{amountText}').";
            return false;
        }

        // 4. Vi: tìm theo tên trong phạm vi user, không tự tạo
        var walletName = row.WalletRaw.Trim();
        if (string.IsNullOrWhiteSpace(walletName) || !walletsByName.TryGetValue(walletName, out var wallet))
        {
            errorReason = $"Không tìm thấy ví '{walletName}' thuộc người dùng.";
            return false;
        }

        // 5. DanhMuc: tìm theo tên trong phạm vi user (mặc định + cá nhân), đúng Type
        var catName = row.CategoryRaw.Trim();
        var catCandidates = string.IsNullOrWhiteSpace(catName) ? Array.Empty<ImportCategoryInfo>() : categoriesByName[catName].ToArray();
        if (catCandidates.Length == 0)
        {
            errorReason = $"Không tìm thấy danh mục '{catName}'.";
            return false;
        }

        // Ưu tiên danh mục cùng Type và của user
        var category = catCandidates
            .OrderByDescending(c => c.UserId.HasValue)
            .FirstOrDefault(c => c.Type == (int)txType);

        if (category is null)
        {
            errorReason = $"Danh mục '{catName}' không khớp loại giao dịch '{(txType == TransactionType.Expense ? "Chi" : "Thu")}'.";
            return false;
        }

        var note = string.IsNullOrWhiteSpace(row.NoteRaw) ? null : row.NoteRaw.Trim();
        if (note != null && note.Length > 1000)
        {
            errorReason = "Ghi chú vượt quá 1000 ký tự.";
            return false;
        }

        parsed = new ParsedImportRow(
            TransactionDateUtc: txDateUtc,
            Amount: amount,
            Type: txType,
            WalletId: wallet.Id,
            WalletCurrency: wallet.Currency,
            CategoryId: category.Id,
            CategoryParentId: category.ParentId,
            CategoryName: category.Name,
            Note: note);
        errorReason = string.Empty;
        return true;
    }

    private static List<List<string>> ReadCsvTable(byte[] fileBytes)
    {
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        string text;
        try
        {
            var span = fileBytes.AsSpan();
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            {
                span = span[3..];
            }
            text = strictUtf8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            throw new ImportValidationException("Tệp CSV không đúng chuẩn mã hóa UTF-8.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ImportValidationException("Tệp CSV rỗng.");
        }

        char delimiter = DetectCsvDelimiter(text);
        return ParseDelimitedText(text, delimiter);
    }

    private static char DetectCsvDelimiter(string text)
    {
        var firstNewLine = text.IndexOfAny(['\r', '\n']);
        var headerLine = firstNewLine >= 0 ? text[..firstNewLine] : text;

        var semiCols = ParseDelimitedText(headerLine, ';');
        var commaCols = ParseDelimitedText(headerLine, ',');
        var semiCount = semiCols.Count > 0 ? semiCols[0].Count : 0;
        var commaCount = commaCols.Count > 0 ? commaCols[0].Count : 0;

        return semiCount > commaCount ? ';' : ',';
    }

    private static List<List<string>> ParseDelimitedText(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"' && sb.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    currentRow.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    currentRow.Add(sb.ToString());
                    sb.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        if (sb.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(sb.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static List<List<string>> ReadXlsxTable(byte[] fileBytes)
    {
        try
        {
            using var ms = new MemoryStream(fileBytes, writable: false);
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.FirstOrDefault()
                ?? throw new ImportValidationException("Tệp Excel không có trang tính (worksheet) nào.");

            var rangeUsed = ws.RangeUsed();
            if (rangeUsed is null)
            {
                throw new ImportValidationException("Tệp Excel không chứa dữ liệu.");
            }

            var rows = new List<List<string>>();
            int firstCol = rangeUsed.FirstColumn().ColumnNumber();
            int lastCol = rangeUsed.LastColumn().ColumnNumber();
            int firstRow = rangeUsed.FirstRow().RowNumber();
            int lastRow = rangeUsed.LastRow().RowNumber();

            for (int r = firstRow; r <= lastRow; r++)
            {
                var rowVals = new List<string>(lastCol - firstCol + 1);
                for (int c = firstCol; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    string val;
                    if (cell.IsEmpty())
                    {
                        val = string.Empty;
                    }
                    else if (cell.DataType == XLDataType.DateTime)
                    {
                        val = cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    else if (cell.DataType == XLDataType.Number)
                    {
                        var num = cell.GetDouble();
                        val = Math.Floor(num) == num
                            ? num.ToString("F0", CultureInfo.InvariantCulture)
                            : num.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        val = cell.GetFormattedString().Trim();
                    }
                    rowVals.Add(val);
                }
                rows.Add(rowVals);
            }

            return rows;
        }
        catch (ImportValidationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ImportValidationException("Không thể đọc tệp Excel (.xlsx). Vui lòng kiểm tra định dạng tệp.");
        }
    }

    private static (int Date, int Amount, int Type, int Category, int Wallet, int Note) ResolveColumns(
        List<string> headers,
        ImportColumnMapping? mapping)
    {
        int FindCol(string? explicitName, params string[] defaultKeys)
        {
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                var target = NormalizeKey(explicitName);
                for (int i = 0; i < headers.Count; i++)
                {
                    if (string.Equals(headers[i].Trim(), explicitName.Trim(), StringComparison.OrdinalIgnoreCase)
                        || NormalizeKey(headers[i]) == target)
                    {
                        return i;
                    }
                }
            }

            for (int i = 0; i < headers.Count; i++)
            {
                var norm = NormalizeKey(headers[i]);
                if (defaultKeys.Contains(norm))
                {
                    return i;
                }
            }
            return -1;
        }

        int idxDate = FindCol(mapping?.Date, "ngay", "date", "transactiondate");
        int idxAmount = FindCol(mapping?.Amount, "sotien", "amount");
        int idxType = FindCol(mapping?.Type, "loai", "type");
        int idxCat = FindCol(mapping?.Category, "danhmuc", "category");
        int idxWallet = FindCol(mapping?.Wallet, "vi", "wallet");
        int idxNote = FindCol(mapping?.Note, "ghichu", "note", "description");

        var missing = new List<string>();
        if (idxDate < 0) missing.Add("Ngay");
        if (idxAmount < 0) missing.Add("SoTien");
        if (idxType < 0) missing.Add("Loai");
        if (idxCat < 0) missing.Add("DanhMuc");
        if (idxWallet < 0) missing.Add("Vi");

        if (missing.Count > 0)
        {
            throw new ImportValidationException($"Thiếu các cột bắt buộc: {string.Join(", ", missing)}.");
        }

        return (idxDate, idxAmount, idxType, idxCat, idxWallet, idxNote);
    }

    private static string GetCell(List<string> cols, int index)
        => index >= 0 && index < cols.Count ? cols[index] : string.Empty;

    private static string NormalizeKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var replaced = input.Replace('đ', 'd').Replace('Đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(replaced.Length);
        foreach (var ch in replaced)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }
        return sb.ToString();
    }
}

