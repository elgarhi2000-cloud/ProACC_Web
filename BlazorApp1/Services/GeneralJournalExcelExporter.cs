using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace BlazorApp1.Services;

public sealed class GeneralJournalExportVm
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
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public List<GeneralJournalLineVm> Rows { get; set; } = new();
}

public static class GeneralJournalExcelExporter
{
    private static readonly XNamespace Ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Create(GeneralJournalExportVm report)
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

    private static XDocument BuildWorksheet(GeneralJournalExportVm report)
    {
        const int headerRow = 7;
        const int firstDataRow = 8;
        var rows = new List<XElement>
        {
            Row(1, TextCell("A1", report.CompanyName, 1)),
            Row(2, TextCell("A2", JoinNonEmpty(" | ", report.CompanyAddress, FormatLabeled("هاتف", report.CompanyPhone)), 2)),
            Row(3, TextCell("A3", JoinNonEmpty(" | ", FormatLabeled("السجل التجاري", report.CommercialRecord), FormatLabeled("الرقم الضريبي", report.TaxNumber)), 2)),
            Row(5, TextCell("A5", "اليومية العامة", 3)),
            Row(6, TextCell("A6", $"الفترة المالية: {report.PeriodName} | من {DisplayDate(report.FromDate)} إلى {DisplayDate(report.ToDate)}", 4)),
            Row(headerRow,
                TextCell("A7", "رقم الحركة", 5), TextCell("B7", "رقم القيد", 5),
                TextCell("C7", "تسلسل القيد", 5), TextCell("D7", "التاريخ", 5),
                TextCell("E7", "رقم الحساب", 5), TextCell("F7", "اسم الحساب", 5),
                TextCell("G7", "الوصف", 5), TextCell("H7", "مدين", 5), TextCell("I7", "دائن", 5))
        };

        var rowNumber = firstDataRow;
        foreach (var item in report.Rows)
        {
            rows.Add(Row(rowNumber,
                TextCell($"A{rowNumber}", item.TransId.ToString(), 6),
                TextCell($"B{rowNumber}", item.GLID?.ToString() ?? string.Empty, 6),
                TextCell($"C{rowNumber}", item.SerialNo?.ToString() ?? string.Empty, 6),
                TextCell($"D{rowNumber}", DisplayDate(item.GLDate), 6),
                TextCell($"E{rowNumber}", item.AccountId?.ToString() ?? string.Empty, 6),
                TextCell($"F{rowNumber}", item.AccountName, 6),
                TextCell($"G{rowNumber}", item.Description, 6),
                DecimalCell($"H{rowNumber}", item.Debit, 7),
                DecimalCell($"I{rowNumber}", item.Credit, 7)));
            rowNumber++;
        }

        var lastDataRow = report.Rows.Count > 0 ? rowNumber - 1 : headerRow;
        var totalRow = rowNumber;
        rows.Add(Row(totalRow,
            TextCell($"A{totalRow}", "الإجمالي", 8),
            report.Rows.Count > 0
                ? FormulaCell($"H{totalRow}", $"SUM(H{firstDataRow}:H{lastDataRow})", report.Rows.Sum(x => x.Debit), 9)
                : DecimalCell($"H{totalRow}", 0m, 9),
            report.Rows.Count > 0
                ? FormulaCell($"I{totalRow}", $"SUM(I{firstDataRow}:I{lastDataRow})", report.Rows.Sum(x => x.Credit), 9)
                : DecimalCell($"I{totalRow}", 0m, 9)));

        var merges = new[] { "A1:I1", "A2:I2", "A3:I3", "A5:I5", "A6:I6", $"A{totalRow}:G{totalRow}" };
        var worksheet = new XElement(Ss + "worksheet",
            new XAttribute(XNamespace.Xmlns + "r", Rel),
            new XElement(Ss + "sheetPr", new XElement(Ss + "pageSetUpPr", new XAttribute("fitToPage", 1))),
            new XElement(Ss + "dimension", new XAttribute("ref", $"A1:I{totalRow}")),
            new XElement(Ss + "sheetViews", new XElement(Ss + "sheetView", new XAttribute("rightToLeft", 1), new XAttribute("showGridLines", 0), new XAttribute("workbookViewId", 0), new XElement(Ss + "pane", new XAttribute("ySplit", headerRow), new XAttribute("topLeftCell", "A8"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            new XElement(Ss + "sheetFormatPr", new XAttribute("defaultRowHeight", 20)),
            new XElement(Ss + "cols", Column(1, 3, 14), Column(4, 4, 14), Column(5, 5, 18), Column(6, 6, 28), Column(7, 7, 46), Column(8, 9, 17)),
            new XElement(Ss + "sheetData", rows),
            new XElement(Ss + "autoFilter", new XAttribute("ref", $"A{headerRow}:I{lastDataRow}")),
            new XElement(Ss + "mergeCells", new XAttribute("count", merges.Length), merges.Select(Merge)),
            new XElement(Ss + "pageMargins", new XAttribute("left", .25), new XAttribute("right", .25), new XAttribute("top", .45), new XAttribute("bottom", .45), new XAttribute("header", .2), new XAttribute("footer", .2)),
            new XElement(Ss + "pageSetup", new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0), new XAttribute("paperSize", 9)));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet);
    }

    private static XElement Row(int index, params XElement[] cells) => new(Ss + "row", new XAttribute("r", index), cells);
    private static XElement Column(int min, int max, double width) => new(Ss + "col", new XAttribute("min", min), new XAttribute("max", max), new XAttribute("width", width), new XAttribute("customWidth", 1));
    private static XElement Merge(string reference) => new(Ss + "mergeCell", new XAttribute("ref", reference));
    private static XElement TextCell(string reference, string? value, int style) => new(Ss + "c", new XAttribute("r", reference), new XAttribute("s", style), new XAttribute("t", "inlineStr"), new XElement(Ss + "is", new XElement(Ss + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? string.Empty)));
    private static XElement DecimalCell(string reference, decimal value, int style) => new(Ss + "c", new XAttribute("r", reference), new XAttribute("s", style), new XElement(Ss + "v", value.ToString(CultureInfo.InvariantCulture)));
    private static XElement FormulaCell(string reference, string formula, decimal value, int style) => new(Ss + "c", new XAttribute("r", reference), new XAttribute("s", style), new XElement(Ss + "f", formula), new XElement(Ss + "v", value.ToString(CultureInfo.InvariantCulture)));
    private static string DisplayDate(DateTime? value) => value?.ToString("dd/MM/yyyy") ?? "-";
    private static string FormatLabeled(string label, string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";
    private static string JoinNonEmpty(string separator, params string?[] values) => string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static void WriteText(ZipArchive archive, string path, string content) { var entry = archive.CreateEntry(path, CompressionLevel.Optimal); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(content); }
    private static void WriteXml(ZipArchive archive, string path, XDocument document) { var entry = archive.CreateEntry(path, CompressionLevel.Optimal); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); document.Save(writer, SaveOptions.DisableFormatting); }

    private const string ContentTypesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""";
    private const string RootRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";
    private const string WorkbookXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><bookViews><workbookView/></bookViews><sheets><sheet name="اليومية العامة" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="191029" calcMode="auto" fullCalcOnLoad="1"/></workbook>""";
    private const string WorkbookRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";
    private const string StylesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00;[Red](#,##0.00);-"/></numFmts><fonts count="4"><font><sz val="10"/><name val="Segoe UI"/></font><font><b/><sz val="18"/><name val="Segoe UI"/></font><font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Segoe UI"/></font><font><b/><sz val="10"/><name val="Segoe UI"/></font></fonts><fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF5948D6"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF0EFFA"/></patternFill></fill></fills><borders count="2"><border/><border><left style="thin"><color rgb="FFDDE1E7"/></left><right style="thin"><color rgb="FFDDE1E7"/></right><top style="thin"><color rgb="FFDDE1E7"/></top><bottom style="thin"><color rgb="FFDDE1E7"/></bottom></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="10"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1"><alignment horizontal="right"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" readingOrder="2"/></xf><xf numFmtId="164" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1"><alignment horizontal="right"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";
}
