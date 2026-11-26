using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ReportService.Models;

namespace ReportService.Services;

public record ExportTransactionRow(
    DateTime TransactionDate,
    int Type,
    string CategoryName,
    decimal Amount,
    string Currency,
    decimal AmountVnd,
    string? Note);

public interface IExportGenerator
{
    byte[] GenerateExcel(DateOnly from, DateOnly to, List<ExportTransactionRow> rows);
    byte[] GeneratePdf(DateOnly from, DateOnly to, List<ExportTransactionRow> rows);
}

public class ExportGenerator : IExportGenerator
{
    private const string EmbeddedFontFamily = "Noto Sans";

    static ExportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;

        var assembly = typeof(ExportGenerator).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
            }
        }
    }

    /// <summary>
    /// Chống CSV / Excel Formula Injection (EX-06 / D60):
    /// Mọi chuỗi bắt đầu bằng = + - @ \t \r được thêm dấu ' và ghi dạng text thuần.
    /// </summary>
    public static string SanitizeCellText(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var first = input[0];
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            return "'" + input;
        }

        return input;
    }

    public byte[] GenerateExcel(DateOnly from, DateOnly to, List<ExportTransactionRow> rows)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1: GiaoDich (bao gồm cả Type 3 theo D60)
        var wsTx = workbook.Worksheets.Add("GiaoDich");
        wsTx.Cell(1, 1).Value = "Ngày giao dịch (VN)";
        wsTx.Cell(1, 2).Value = "Loại";
        wsTx.Cell(1, 3).Value = "Danh mục";
        wsTx.Cell(1, 4).Value = "Số tiền";
        wsTx.Cell(1, 5).Value = "Tiền tệ";
        wsTx.Cell(1, 6).Value = "Ghi chú";

        var headerRange = wsTx.Range(1, 1, 1, 6);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int rowIdx = i + 2;
            var vnDate = VietnamTimeHelper.ToVietnamTime(r.TransactionDate);
            var typeLabel = r.Type switch
            {
                1 => "Chi tiêu",
                2 => "Thu nhập",
                3 => "Chuyển tiền",
                _ => "Khác"
            };

            wsTx.Cell(rowIdx, 1).Value = vnDate.ToString("yyyy-MM-dd HH:mm");
            wsTx.Cell(rowIdx, 2).Value = typeLabel;
            wsTx.Cell(rowIdx, 3).Value = SanitizeCellText(string.IsNullOrWhiteSpace(r.CategoryName) ? "Khác" : r.CategoryName);
            wsTx.Cell(rowIdx, 4).Value = r.Amount;
            wsTx.Cell(rowIdx, 5).Value = SanitizeCellText(r.Currency);
            wsTx.Cell(rowIdx, 6).Value = SanitizeCellText(r.Note);
        }

        wsTx.Columns().AdjustToContents();

        // Sheet 2: TongHop (không tính Type 3 - khớp dashboard D60)
        var wsSummary = workbook.Worksheets.Add("TongHop");
        var reportRows = rows.Where(x => x.Type == 1 || x.Type == 2).ToList();
        var totalIncome = reportRows.Where(x => x.Type == 2).Sum(x => x.AmountVnd);
        var totalExpense = reportRows.Where(x => x.Type == 1).Sum(x => x.AmountVnd);
        var net = totalIncome - totalExpense;

        wsSummary.Cell(1, 1).Value = "Kỳ báo cáo";
        wsSummary.Cell(1, 2).Value = $"{from:yyyy-MM-dd} đến {to:yyyy-MM-dd}";

        wsSummary.Cell(2, 1).Value = "Tổng thu (VND)";
        wsSummary.Cell(2, 2).Value = totalIncome;

        wsSummary.Cell(3, 1).Value = "Tổng chi (VND)";
        wsSummary.Cell(3, 2).Value = totalExpense;

        wsSummary.Cell(4, 1).Value = "Chênh lệch (VND)";
        wsSummary.Cell(4, 2).Value = net;

        wsSummary.Range(1, 1, 4, 1).Style.Font.Bold = true;

        wsSummary.Cell(6, 1).Value = "Danh mục chi";
        wsSummary.Cell(6, 2).Value = "Tổng tiền (VND)";
        wsSummary.Range(6, 1, 6, 2).Style.Font.Bold = true;

        var byCat = reportRows
            .Where(x => x.Type == 1)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CategoryName) ? "Khác" : x.CategoryName)
            .Select(g => new { Name = g.Key, Total = g.Sum(x => x.AmountVnd) })
            .OrderByDescending(g => g.Total)
            .ToList();

        for (int i = 0; i < byCat.Count; i++)
        {
            wsSummary.Cell(7 + i, 1).Value = SanitizeCellText(byCat[i].Name);
            wsSummary.Cell(7 + i, 2).Value = byCat[i].Total;
        }

        wsSummary.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        NormalizeOpenXmlNamespace(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Chuẩn hóa namespace SpreadsheetML về mặc định (bỏ prefix x:) để tương thích mọi parser OOXML.
    /// </summary>
    private static void NormalizeOpenXmlNamespace(MemoryStream ms)
    {
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true);
        var entries = zip.Entries
            .Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        && (e.FullName == "xl/workbook.xml" || e.FullName.StartsWith("xl/worksheets/") || e.FullName == "xl/sharedStrings.xml"))
            .ToList();

        foreach (var entry in entries)
        {
            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                xml = reader.ReadToEnd();
            }

            if (!xml.Contains("xmlns:x=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"", StringComparison.Ordinal))
            {
                continue;
            }

            xml = xml
                .Replace("xmlns:x=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"", "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"", StringComparison.Ordinal)
                .Replace("<x:", "<", StringComparison.Ordinal)
                .Replace("</x:", "</", StringComparison.Ordinal);

            entry.Delete();
            var newEntry = zip.CreateEntry(entry.FullName);
            using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
    }

    public byte[] GeneratePdf(DateOnly from, DateOnly to, List<ExportTransactionRow> rows)
    {
        return CreatePdfDocument(from, to, rows).GeneratePdf();
    }

    public Document CreatePdfDocument(DateOnly from, DateOnly to, List<ExportTransactionRow> rows)
    {
        var reportRows = rows.Where(x => x.Type == 1 || x.Type == 2).ToList();
        var totalIncome = reportRows.Where(x => x.Type == 2).Sum(x => x.AmountVnd);
        var totalExpense = reportRows.Where(x => x.Type == 1).Sum(x => x.AmountVnd);
        var net = totalIncome - totalExpense;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontFamily(EmbeddedFontFamily).FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("BÁO CÁO TÀI CHÍNH SMARTWALLET").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Kỳ báo cáo: {from:dd/MM/yyyy} – {to:dd/MM/yyyy}").FontSize(11).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    // Bảng tổng hợp
                    col.Item().PaddingBottom(12).Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Tổng thu").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{totalIncome:N0} VND").Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Tổng chi").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{totalExpense:N0} VND").Bold().FontColor(Colors.Red.Darken2);
                        });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Chênh lệch").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{net:N0} VND").Bold().FontColor(Colors.Blue.Darken2);
                        });
                    });

                    col.Item().PaddingBottom(6).Text("Chi tiết giao dịch").FontSize(12).Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(85);
                            columns.ConstantColumn(65);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(90);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Ngày").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Loại").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Danh mục").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Ghi chú").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Số tiền").Bold();
                        });

                        foreach (var r in rows)
                        {
                            var vn = VietnamTimeHelper.ToVietnamTime(r.TransactionDate);
                            var typeStr = r.Type switch { 1 => "Chi", 2 => "Thu", 3 => "Chuyển", _ => "Khác" };
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(vn.ToString("dd/MM/yyyy"));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(typeStr);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(string.IsNullOrWhiteSpace(r.CategoryName) ? "Khác" : r.CategoryName);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(r.Note ?? string.Empty);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(4).AlignRight().Text($"{r.Amount:N0} {r.Currency}");
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Trang ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        });
    }
}

