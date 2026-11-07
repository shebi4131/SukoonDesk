using System.Globalization;
using System.Text;

namespace SukoonDesk.Api.Infrastructure;

public sealed record ReportTotals(int Total, int Open, int Overdue, int Resolved, double SlaCompliance, double AverageResolutionHours, double Satisfaction);
public sealed record ReportSlice(string Name, int Value, Guid? Id = null);
public sealed record ReportTrend(string Label, int Created, int Resolved);
public sealed record ReportRow(string Number, string Title, string Category, string Status, string Priority, string Resident, string Assignee, DateTimeOffset CreatedAt, DateTimeOffset DueAt);
public sealed record ReportData(
    string Society,
    DateTimeOffset From,
    DateTimeOffset To,
    ReportTotals Totals,
    IReadOnlyList<ReportSlice> ByStatus,
    IReadOnlyList<ReportSlice> ByCategory,
    IReadOnlyList<ReportSlice> ByPriority,
    IReadOnlyList<ReportSlice> ByPhase,
    IReadOnlyList<ReportSlice> ByAssignee,
    IReadOnlyList<ReportSlice> Backlog,
    IReadOnlyList<ReportTrend> Trend,
    IReadOnlyList<ReportRow> Recent,
    IReadOnlyList<object> Categories,
    string[] Statuses,
    string[] Priorities,
    string[] Phases);

public static class ReportPdfBuilder
{
    private const double Width = 842;
    private const double Height = 595;

    public static byte[] Build(ReportData report)
    {
        var pages = new List<string> { FirstPage(report) };
        var remaining = report.Recent.Skip(7).ToList();
        for (var index = 0; index < remaining.Count; index += 14)
            pages.Add(ContinuationPage(report, remaining.Skip(index).Take(14).ToList(), pages.Count + 1));
        return WriteDocument(pages);
    }

    private static string FirstPage(ReportData report)
    {
        var canvas = new Canvas();
        canvas.Fill(0, 0, Width, Height, "F7FAFC");
        canvas.Fill(0, 515, Width, 80, "155E75");
        canvas.Text(34, 558, 22, "SukoonDesk", true, "FFFFFF");
        canvas.Text(34, 535, 11, "SERVICE PERFORMANCE REPORT", true, "CFFAFE");
        canvas.Text(808, 558, 15, report.Society, true, "FFFFFF", true);
        canvas.Text(808, 537, 9, $"{report.From:dd MMM yyyy} - {report.To:dd MMM yyyy}", false, "CFFAFE", true);

        var metrics = new[]
        {
            ("TOTAL", report.Totals.Total.ToString(CultureInfo.InvariantCulture), "2563EB"),
            ("OPEN", report.Totals.Open.ToString(CultureInfo.InvariantCulture), "7C3AED"),
            ("OVERDUE", report.Totals.Overdue.ToString(CultureInfo.InvariantCulture), "DC2626"),
            ("SLA", $"{report.Totals.SlaCompliance:0.#}%", "15803D"),
            ("SATISFACTION", $"{report.Totals.Satisfaction:0.#}/5", "D97706")
        };
        for (var i = 0; i < metrics.Length; i++)
        {
            var x = 34 + i * 156;
            canvas.RoundCard(x, 448, 144, 52);
            canvas.Text(x + 13, 481, 7, metrics[i].Item1, true, "64748B");
            canvas.Text(x + 13, 459, 18, metrics[i].Item2, true, metrics[i].Item3);
        }

        canvas.Text(34, 422, 12, "STATUS PORTFOLIO", true, "0F2F3A");
        DrawBars(canvas, 34, 402, 340, report.ByStatus.Take(6).ToList(), "155E75");
        canvas.Text(430, 422, 12, "TOP CATEGORIES", true, "0F2F3A");
        DrawBars(canvas, 430, 402, 378, report.ByCategory.Take(6).ToList(), "2BA8BB");

        DrawMiniSummary(canvas, 34, 272, 240, "PRIORITY MIX", report.ByPriority);
        DrawMiniSummary(canvas, 301, 272, 240, "PHASE HOTSPOTS", report.ByPhase);
        DrawMiniSummary(canvas, 568, 272, 240, "BACKLOG AGE", report.Backlog);

        canvas.Text(34, 238, 12, "RECENT COMPLAINTS", true, "0F2F3A");
        canvas.Text(808, 239, 8, $"{report.Totals.Resolved} resolved | {report.Totals.AverageResolutionHours:0.#}h average resolution", false, "64748B", true);
        DrawTable(canvas, report.Recent.Take(7).ToList(), 218);
        Footer(canvas, 1);
        return canvas.ToString();
    }

    private static string ContinuationPage(ReportData report, IReadOnlyList<ReportRow> rows, int page)
    {
        var canvas = new Canvas();
        canvas.Fill(0, 0, Width, Height, "F7FAFC");
        canvas.Fill(0, 530, Width, 65, "155E75");
        canvas.Text(34, 562, 18, "SukoonDesk - Complaint detail", true, "FFFFFF");
        canvas.Text(808, 561, 10, report.Society, false, "CFFAFE", true);
        DrawTable(canvas, rows, 505, 27);
        Footer(canvas, page);
        return canvas.ToString();
    }

    private static void DrawBars(Canvas canvas, double x, double y, double width, IReadOnlyList<ReportSlice> rows, string color)
    {
        var max = Math.Max(rows.Select(item => item.Value).DefaultIfEmpty(1).Max(), 1);
        if (rows.Count == 0) canvas.Text(x, y - 15, 9, "No matching data for this period.", false, "94A3B8");
        for (var index = 0; index < rows.Count; index++)
        {
            var currentY = y - index * 27;
            canvas.Text(x, currentY, 8, rows[index].Name, false, "334155");
            canvas.Text(x + width, currentY, 8, rows[index].Value.ToString(CultureInfo.InvariantCulture), true, "334155", true);
            canvas.Fill(x, currentY - 10, width, 5, "E2E8F0");
            canvas.Fill(x, currentY - 10, Math.Max(3, width * rows[index].Value / max), 5, color);
        }
    }

    private static void DrawTable(Canvas canvas, IReadOnlyList<ReportRow> rows, double top, double rowHeight = 22)
    {
        canvas.Fill(34, top - 18, 774, 18, "E8F1F3");
        canvas.Text(43, top - 12, 7, "NUMBER / CONCERN", true, "155E75");
        canvas.Text(360, top - 12, 7, "CATEGORY", true, "155E75");
        canvas.Text(510, top - 12, 7, "RESIDENT", true, "155E75");
        canvas.Text(650, top - 12, 7, "STATUS", true, "155E75");
        canvas.Text(796, top - 12, 7, "CREATED", true, "155E75", true);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var y = top - 18 - (index + 1) * rowHeight;
            if (index % 2 == 0) canvas.Fill(34, y, 774, rowHeight, "FFFFFF");
            canvas.Text(43, y + 13, 7, $"{row.Number}  {Trim(row.Title, 38)}", index == 0, "0F172A");
            canvas.Text(360, y + 13, 7, Trim(row.Category, 22), false, "475569");
            canvas.Text(510, y + 13, 7, Trim(row.Resident, 20), false, "475569");
            canvas.Text(650, y + 13, 7, row.Status, true, StatusColor(row.Status));
            canvas.Text(796, y + 13, 7, row.CreatedAt.ToString("dd MMM yy"), false, "64748B", true);
        }
        if (rows.Count == 0) canvas.Text(421, top - 52, 10, "No complaints match the selected filters.", false, "94A3B8", true);
    }

    private static void DrawMiniSummary(Canvas canvas, double x, double y, double width, string title, IReadOnlyList<ReportSlice> rows)
    {
        canvas.RoundCard(x, y, width, 66);
        canvas.Text(x + 13, y + 47, 8, title, true, "155E75");
        var visible = rows.Where(item => item.Value > 0).Take(3).ToList();
        if (visible.Count == 0)
        {
            canvas.Text(x + 13, y + 25, 8, "No matching activity", false, "94A3B8");
            return;
        }
        var summary = string.Join("   ", visible.Select(item => $"{item.Name}: {item.Value}"));
        canvas.Text(x + 13, y + 24, 8, Trim(summary, 48), false, "334155");
    }

    private static void Footer(Canvas canvas, int page)
    {
        canvas.Line(34, 29, 808, 29, "CBD5E1");
        canvas.Text(34, 17, 7, $"Generated {DateTimeOffset.Now:dd MMM yyyy HH:mm}", false, "94A3B8");
        canvas.Text(421, 17, 7, "Private society operations report", false, "94A3B8", true);
        canvas.Text(808, 17, 7, $"Page {page}", false, "94A3B8", true);
    }

    private static string StatusColor(string status) => status switch
    {
        "Resolved" or "Closed" => "15803D",
        "Overdue" or "Rejected" => "DC2626",
        "InProgress" or "Assigned" => "2563EB",
        _ => "D97706"
    };

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "...";

    private static byte[] WriteDocument(IReadOnlyList<string> pageStreams)
    {
        var objects = new List<byte[]>();
        var pageObjectNumbers = new List<int>();
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add([]);
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"));
        foreach (var stream in pageStreams)
        {
            var pageObject = objects.Count + 1;
            var contentObject = pageObject + 1;
            pageObjectNumbers.Add(pageObject);
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Width} {Height}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObject} 0 R >>"));
            var content = Encoding.ASCII.GetBytes(stream);
            var prefix = Encoding.ASCII.GetBytes($"<< /Length {content.Length} >>\nstream\n");
            var suffix = Encoding.ASCII.GetBytes("\nendstream");
            objects.Add(prefix.Concat(content).Concat(suffix).ToArray());
        }
        objects[1] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(x => $"{x} 0 R"))}] /Count {pageStreams.Count} >>");

        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n%SukoonDesk\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            Write(output, $"{index + 1} 0 obj\n");
            output.Write(objects[index]);
            Write(output, "\nendobj\n");
        }
        var xref = output.Position;
        Write(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(output, $"{offset:0000000000} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }

    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

    private sealed class Canvas
    {
        private readonly StringBuilder _content = new();
        public void Fill(double x, double y, double width, double height, string color) => _content.AppendLine($"{Rgb(color)} rg {N(x)} {N(y)} {N(width)} {N(height)} re f");
        public void RoundCard(double x, double y, double width, double height)
        {
            Fill(x, y, width, height, "FFFFFF");
            _content.AppendLine($"{Rgb("DCE7EA")} RG 0.7 w {N(x)} {N(y)} {N(width)} {N(height)} re S");
        }
        public void Line(double x1, double y1, double x2, double y2, string color) => _content.AppendLine($"{Rgb(color)} RG 0.6 w {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S");
        public void Text(double x, double y, double size, string value, bool bold, string color, bool right = false)
        {
            var safe = Escape(value);
            var estimated = safe.Length * size * .5;
            if (right) x -= estimated;
            _content.AppendLine($"BT /{(bold ? "F2" : "F1")} {N(size)} Tf {Rgb(color)} rg {N(x)} {N(y)} Td ({safe}) Tj ET");
        }
        public override string ToString() => _content.ToString();
        private static string Escape(string value) => string.Concat(value.Normalize(NormalizationForm.FormD).Where(c => c <= 127 && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        private static string Rgb(string hex)
        {
            var r = Convert.ToInt32(hex[..2], 16) / 255d;
            var g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255d;
            var b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255d;
            return $"{N(r)} {N(g)} {N(b)}";
        }
        private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
