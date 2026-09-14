using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace BlazorApp1.Services;

public sealed class TrialBalanceExportVm
{
    public ReportDesign? Design { get; set; }
    public byte[]? CompanyLogo { get; set; }
    public string? PrintedBy { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public string CommercialRecord { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string PeriodName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public bool IncludeLevels { get; set; }
    public List<TrialBalanceRowVm> Rows { get; set; } = new();
}

public static class TrialBalanceExcelExporter
{
    private static readonly XNamespace Ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Create(TrialBalanceExportVm report)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "[Content_Types].xml", ContentTypesXml);
            WriteText(archive, "_rels/.rels", RootRelationshipsXml);
            WriteText(archive, "xl/workbook.xml", WorkbookXml);
            WriteText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
            WriteText(archive, "xl/styles.xml", StylesXml);
            WriteXml(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(report));
        }
        return output.ToArray();
    }

    private static XDocument BuildWorksheet(TrialBalanceExportVm report)
    {
        var headers = new List<string> { "رقم الحساب", "اسم الحساب" };
        if (report.IncludeLevels) headers.AddRange(new[] { "المستوى 1", "المستوى 2", "المستوى 3", "المستوى 4", "المستوى 5" });
        headers.AddRange(new[] { "رصيد افتتاحي", "مدين الفترة", "دائن الفترة", "رصيد الفترة", "إجمالي مدين", "إجمالي دائن", "رصيد ختامي" });

        var lastColumn = ColumnName(headers.Count);
        var openingColumnIndex = report.IncludeLevels ? 8 : 3;
        var openingColumn = ColumnName(openingColumnIndex);
        var periodDebitColumn = ColumnName(openingColumnIndex + 1);
        var periodCreditColumn = ColumnName(openingColumnIndex + 2);
        var periodBalanceColumn = ColumnName(openingColumnIndex + 3);
        var totalDebitColumn = ColumnName(openingColumnIndex + 4);
        var totalCreditColumn = ColumnName(openingColumnIndex + 5);
        var closingColumn = ColumnName(openingColumnIndex + 6);
        const int headerRow = 8;
        var firstDataRow = headerRow + 1;

        var rows = new List<XElement>
        {
            Row(1, TextCell("A1", report.CompanyName, 1)),
            Row(2, TextCell("A2", JoinNonEmpty(" | ", report.CompanyAddress, FormatLabeled("هاتف", report.CompanyPhone)), 2)),
            Row(3, TextCell("A3", JoinNonEmpty(" | ", FormatLabeled("السجل التجاري", report.CommercialRecord), FormatLabeled("الرقم الضريبي", report.TaxNumber)), 2)),
            Row(5, TextCell("A5", "ميزان المراجعة", 3)),
            Row(6, TextCell("A6", $"الفترة المالية: {report.PeriodName} | من {report.FromDate:dd/MM/yyyy} إلى {report.ToDate:dd/MM/yyyy}", 4)),
            Row(headerRow, headers.Select((header, index) => TextCell($"{ColumnName(index + 1)}{headerRow}", header, 5)).ToArray())
        };

        var rowNumber = firstDataRow;
        foreach (var item in report.Rows)
        {
            var cells = new List<XElement>
            {
                TextCell($"A{rowNumber}", item.AccountId.ToString(), 6),
                TextCell($"B{rowNumber}", item.AccountName, 6)
            };
            if (report.IncludeLevels)
            {
                cells.AddRange(new[]
                {
                    TextCell($"C{rowNumber}", item.Level1, 6), TextCell($"D{rowNumber}", item.Level2, 6),
                    TextCell($"E{rowNumber}", item.Level3, 6), TextCell($"F{rowNumber}", item.Level4, 6),
                    TextCell($"G{rowNumber}", item.Level5, 6)
                });
            }

            cells.Add(DecimalCell($"{openingColumn}{rowNumber}", item.OpeningBalance, 7));
            cells.Add(DecimalCell($"{periodDebitColumn}{rowNumber}", item.PeriodDebit, 7));
            cells.Add(DecimalCell($"{periodCreditColumn}{rowNumber}", item.PeriodCredit, 7));
            cells.Add(FormulaCell($"{periodBalanceColumn}{rowNumber}", $"{periodDebitColumn}{rowNumber}-{periodCreditColumn}{rowNumber}", item.PeriodBalance, 7));
            cells.Add(FormulaCell($"{totalDebitColumn}{rowNumber}", $"{periodDebitColumn}{rowNumber}+IF({openingColumn}{rowNumber}>0,{openingColumn}{rowNumber},0)", item.TotalDebit, 7));
            cells.Add(FormulaCell($"{totalCreditColumn}{rowNumber}", $"{periodCreditColumn}{rowNumber}+IF({openingColumn}{rowNumber}<0,-{openingColumn}{rowNumber},0)", item.TotalCredit, 7));
            cells.Add(FormulaCell($"{closingColumn}{rowNumber}", $"{totalDebitColumn}{rowNumber}-{totalCreditColumn}{rowNumber}", item.ClosingBalance, 8));
            rows.Add(Row(rowNumber, cells.ToArray()));
            rowNumber++;
        }

        var hasDataRows = report.Rows.Count > 0;
        var lastDataRow = hasDataRows ? rowNumber - 1 : headerRow;
        var totalRow = rowNumber;
        var totalCells = new List<XElement> { TextCell($"A{totalRow}", "الإجمالي", 9) };
        var totals = new[]
        {
            report.Rows.Sum(x => x.OpeningBalance), report.Rows.Sum(x => x.PeriodDebit), report.Rows.Sum(x => x.PeriodCredit),
            report.Rows.Sum(x => x.PeriodBalance), report.Rows.Sum(x => x.TotalDebit), report.Rows.Sum(x => x.TotalCredit), report.Rows.Sum(x => x.ClosingBalance)
        };
        for (var index = 0; index < 7; index++)
        {
            var column = ColumnName(openingColumnIndex + index);
            totalCells.Add(hasDataRows
                ? FormulaCell($"{column}{totalRow}", $"SUM({column}{firstDataRow}:{column}{lastDataRow})", totals[index], index == 6 ? 11 : 10)
                : DecimalCell($"{column}{totalRow}", 0m, index == 6 ? 11 : 10));
        }
        rows.Add(Row(totalRow, totalCells.ToArray()));

        var columns = new List<XElement> { Column(1, 1, 16), Column(2, 2, 30) };
        if (report.IncludeLevels) columns.Add(Column(3, 7, 21));
        columns.Add(Column(openingColumnIndex, openingColumnIndex + 6, 17));

        var merges = new[] { $"A1:{lastColumn}1", $"A2:{lastColumn}2", $"A3:{lastColumn}3", $"A5:{lastColumn}5", $"A6:{lastColumn}6", $"A{totalRow}:{ColumnName(openingColumnIndex - 1)}{totalRow}" };
        var worksheet = new XElement(Ss + "worksheet",
            new XAttribute(XNamespace.Xmlns + "r", Rel),
            new XElement(Ss + "sheetPr", new XElement(Ss + "pageSetUpPr", new XAttribute("fitToPage", 1))),
            new XElement(Ss + "dimension", new XAttribute("ref", $"A1:{lastColumn}{totalRow}")),
            new XElement(Ss + "sheetViews", new XElement(Ss + "sheetView", new XAttribute("rightToLeft", 1), new XAttribute("showGridLines", 0), new XAttribute("workbookViewId", 0), new XElement(Ss + "pane", new XAttribute("ySplit", headerRow), new XAttribute("topLeftCell", $"A{firstDataRow}"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            new XElement(Ss + "sheetFormatPr", new XAttribute("defaultRowHeight", 20)),
            new XElement(Ss + "cols", columns),
            new XElement(Ss + "sheetData", rows),
            new XElement(Ss + "autoFilter", new XAttribute("ref", $"A{headerRow}:{lastColumn}{lastDataRow}")),
            new XElement(Ss + "mergeCells", new XAttribute("count", merges.Length), merges.Select(Merge)),
            new XElement(Ss + "pageMargins", new XAttribute("left", .25), new XAttribute("right", .25), new XAttribute("top", .45), new XAttribute("bottom", .45), new XAttribute("header", .2), new XAttribute("footer", .2)),
            new XElement(Ss + "pageSetup", new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0), new XAttribute("paperSize", report.IncludeLevels ? 8 : 9)));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet);
    }

    private static string ColumnName(int number)
    {
        var result = string.Empty;
        while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; }
        return result;
    }

    private static XElement Row(int index, params XElement[] cells) => new(Ss + "row", new XAttribute("r", index), cells);
    private static XElement Column(int min, int max, double width) => new(Ss + "col", new XAttribute("min", min), new XAttribute("max", max), new XAttribute("width", width), new XAttribute("customWidth", 1));
    private static XElement Merge(string reference) => new(Ss + "mergeCell", new XAttribute("ref", reference));
    private static XElement TextCell(string reference, string? value, int style) => new(Ss + "c", new XAttribute("r", reference), new XAttribute("s", style), new XAttribute("t", "inlineStr"), new XElement(Ss + "is", new XElement(Ss + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? string.Empty)));
    private static XElement DecimalCell(string reference, decimal value, int style) => new(Ss + "c", new XAttribute("r", reference), new XAttribute("s", style), new XElement(Ss + "v", value.ToString(CultureInfo.InvariantCulture)));
    private static XElement FormulaCell(string reference, string formula, decimal value, int style) => new(Ss + "c", new XAttribute("r", reference), new XAttribute("s", style), new XElement(Ss + "f", formula), new XElement(Ss + "v", value.ToString(CultureInfo.InvariantCulture)));
    private static string FormatLabeled(string label, string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";
    private static string JoinNonEmpty(string separator, params string?[] values) => string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static void WriteText(ZipArchive archive, string path, string content) { var entry = archive.CreateEntry(path, CompressionLevel.Optimal); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(content); }
    private static void WriteXml(ZipArchive archive, string path, XDocument document) { var entry = archive.CreateEntry(path, CompressionLevel.Optimal); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); document.Save(writer, SaveOptions.DisableFormatting); }

    private const string ContentTypesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""";
    private const string RootRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";
    private const string WorkbookXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><bookViews><workbookView/></bookViews><sheets><sheet name="ميزان المراجعة" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="191029" calcMode="auto" fullCalcOnLoad="1"/></workbook>""";
    private const string WorkbookRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";
    private const string StylesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00;[Red](#,##0.00);-"/></numFmts><fonts count="4"><font><sz val="10"/><name val="Segoe UI"/></font><font><b/><sz val="18"/><color rgb="FF27223F"/><name val="Segoe UI"/></font><font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Segoe UI"/></font><font><b/><sz val="10"/><name val="Segoe UI"/></font></fonts><fills count="5"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF5948D6"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF0EFFA"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF7F8FA"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border/><border><left style="thin"><color rgb="FFDDE1E7"/></left><right style="thin"><color rgb="FFDDE1E7"/></right><top style="thin"><color rgb="FFDDE1E7"/></top><bottom style="thin"><color rgb="FFDDE1E7"/></bottom></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="12"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2" wrapText="1"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2" wrapText="1"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="164" fontId="3" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2"/></xf><xf numFmtId="164" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="164" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";
}
