using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace BlazorApp1.Services;

public sealed class JournalEntryExportVm
{
    public ReportDesign? Design { get; set; }
    public byte[]? CompanyLogo { get; set; }
    public string? PrintedBy { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public string CommercialRecord { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public int EntryId { get; set; }
    public int? SerialNumber { get; set; }
    public DateTime? EntryDate { get; set; }
    public string Period { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public string Card { get; set; } = string.Empty;
    public string Bank { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Center1 { get; set; } = string.Empty;
    public string Center2 { get; set; } = string.Empty;
    public string? UserInfo { get; set; }
    public bool IsArchived { get; set; }
    public List<JournalEntryExportLineVm> Lines { get; set; } = new();
}

public sealed class JournalEntryExportLineVm
{
    public int Number { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? DocumentReference { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string Center { get; set; } = string.Empty;
}

public static class JournalEntryExcelExporter
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Create(JournalEntryExportVm report)
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

    private static XDocument BuildWorksheet(JournalEntryExportVm report)
    {
        var rows = new List<XElement>
        {
            Row(1, TextCell("A1", report.CompanyName, 1)),
            Row(2, TextCell("A2", JoinNonEmpty(" | ", report.CompanyAddress, FormatLabeled("هاتف", report.CompanyPhone)), 2)),
            Row(3, TextCell("A3", JoinNonEmpty(" | ", FormatLabeled("السجل التجاري", report.CommercialRecord), FormatLabeled("الرقم الضريبي", report.TaxNumber)), 2)),
            Row(5, TextCell("A5", "قيد يومية", 3)),
            Row(7,
                TextCell("A7", "رقم القيد", 4), NumberCell("B7", report.EntryId, 5),
                TextCell("C7", "تسلسل القيد", 4), NullableNumberCell("D7", report.SerialNumber, 5),
                TextCell("E7", "تاريخ القيد", 4), DateCell("F7", report.EntryDate, 10),
                TextCell("G7", "الفترة المالية", 4), TextCell("H7", report.Period, 5)),
            Row(8,
                TextCell("A8", "نوع القيد", 4), TextCell("B8", report.Source, 5),
                TextCell("D8", "رقم المرجع", 4), TextCell("E8", report.Reference, 5),
                TextCell("F8", "حالة الأرشفة", 4), TextCell("G8", report.IsArchived ? "مؤرشف" : "غير مؤرشف", 5)),
            Row(9, TextCell("A9", "بيان القيد", 4), TextCell("B9", report.Description, 5)),
            Row(10,
                TextCell("A10", "جهة التعامل", 4), TextCell("B10", report.Card, 5),
                TextCell("D10", "اسم البنك", 4), TextCell("E10", report.Bank, 5),
                TextCell("G10", "طريقة الدفع", 4), TextCell("H10", report.PaymentMethod, 5)),
            Row(11,
                TextCell("A11", "مركز 1", 4), TextCell("B11", report.Center1, 5),
                TextCell("D11", "مركز 2", 4), TextCell("E11", report.Center2, 5),
                TextCell("G11", "المستخدم", 4), TextCell("H11", report.UserInfo, 5)),
            Row(13,
                TextCell("A13", "#", 6), TextCell("B13", "رقم الحساب", 6), TextCell("C13", "اسم الحساب", 6),
                TextCell("D13", "الوصف", 6), TextCell("E13", "مدين", 6), TextCell("F13", "دائن", 6),
                TextCell("G13", "رقم المستند", 6), TextCell("H13", "تاريخ المستند", 6), TextCell("I13", "مركز فرعي", 6))
        };

        var rowNumber = 14;
        foreach (var line in report.Lines)
        {
            rows.Add(Row(rowNumber,
                NumberCell($"A{rowNumber}", line.Number, 7),
                TextCell($"B{rowNumber}", line.AccountCode, 7),
                TextCell($"C{rowNumber}", line.AccountName, 7),
                TextCell($"D{rowNumber}", line.Description, 7),
                DecimalCell($"E{rowNumber}", line.Debit, 8),
                DecimalCell($"F{rowNumber}", line.Credit, 8),
                TextCell($"G{rowNumber}", line.DocumentReference, 7),
                DateCell($"H{rowNumber}", line.DocumentDate, 10),
                TextCell($"I{rowNumber}", line.Center, 7)));
            rowNumber++;
        }

        rows.Add(Row(rowNumber,
            TextCell($"A{rowNumber}", "الإجمالي", 9),
            DecimalCell($"E{rowNumber}", report.Lines.Sum(x => x.Debit), 11),
            DecimalCell($"F{rowNumber}", report.Lines.Sum(x => x.Credit), 11)));

        var worksheet = new XElement(Spreadsheet + "worksheet",
            new XAttribute(XNamespace.Xmlns + "r", Relationships),
            new XElement(Spreadsheet + "sheetPr", new XElement(Spreadsheet + "pageSetUpPr", new XAttribute("fitToPage", 1))),
            new XElement(Spreadsheet + "sheetViews",
                new XElement(Spreadsheet + "sheetView",
                    new XAttribute("rightToLeft", 1),
                    new XAttribute("workbookViewId", 0),
                    new XElement(Spreadsheet + "pane", new XAttribute("ySplit", 13), new XAttribute("topLeftCell", "A14"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            new XElement(Spreadsheet + "sheetFormatPr", new XAttribute("defaultRowHeight", 20)),
            new XElement(Spreadsheet + "cols",
                Column(1, 1, 5), Column(2, 2, 16), Column(3, 3, 28), Column(4, 4, 42),
                Column(5, 6, 15), Column(7, 7, 18), Column(8, 8, 16), Column(9, 9, 22)),
            new XElement(Spreadsheet + "sheetData", rows),
            new XElement(Spreadsheet + "mergeCells", new XAttribute("count", 16),
                Merge("A1:I1"), Merge("A2:I2"), Merge("A3:I3"), Merge("A5:I5"), Merge("H7:I7"),
                Merge("B8:C8"), Merge("G8:I8"), Merge("B9:I9"), Merge("B10:C10"), Merge("E10:F10"),
                Merge("H10:I10"), Merge("B11:C11"), Merge("E11:F11"), Merge("H11:I11"),
                Merge($"A{rowNumber}:D{rowNumber}"), Merge($"G{rowNumber}:I{rowNumber}")),
            new XElement(Spreadsheet + "pageMargins",
                new XAttribute("left", 0.3), new XAttribute("right", 0.3), new XAttribute("top", 0.5),
                new XAttribute("bottom", 0.5), new XAttribute("header", 0.2), new XAttribute("footer", 0.2)),
            new XElement(Spreadsheet + "pageSetup", new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0), new XAttribute("paperSize", 9)));

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet);
    }

    private static XElement Row(int index, params XElement[] cells)
        => new(Spreadsheet + "row", new XAttribute("r", index), cells);

    private static XElement Column(int min, int max, double width)
        => new(Spreadsheet + "col", new XAttribute("min", min), new XAttribute("max", max), new XAttribute("width", width), new XAttribute("customWidth", 1));

    private static XElement Merge(string reference)
        => new(Spreadsheet + "mergeCell", new XAttribute("ref", reference));

    private static XElement TextCell(string reference, string? value, int style)
        => new(Spreadsheet + "c",
            new XAttribute("r", reference), new XAttribute("s", style), new XAttribute("t", "inlineStr"),
            new XElement(Spreadsheet + "is",
                new XElement(Spreadsheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? string.Empty)));

    private static XElement NumberCell(string reference, int value, int style)
        => NumericCell(reference, value.ToString(CultureInfo.InvariantCulture), style);

    private static XElement NullableNumberCell(string reference, int? value, int style)
        => value.HasValue ? NumberCell(reference, value.Value, style) : TextCell(reference, string.Empty, style);

    private static XElement DecimalCell(string reference, decimal value, int style)
        => NumericCell(reference, value.ToString(CultureInfo.InvariantCulture), style);

    private static XElement DateCell(string reference, DateTime? value, int style)
        => value.HasValue
            ? NumericCell(reference, value.Value.Date.ToOADate().ToString(CultureInfo.InvariantCulture), style)
            : TextCell(reference, string.Empty, style);

    private static XElement NumericCell(string reference, string value, int style)
        => new(Spreadsheet + "c", new XAttribute("r", reference), new XAttribute("s", style), new XElement(Spreadsheet + "v", value));

    private static string FormatLabeled(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";

    private static string JoinNonEmpty(string separator, params string?[] values)
        => string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string RootRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <bookViews><workbookView/></bookViews>
          <sheets><sheet name="قيد اليومية" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private const string WorkbookRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="2"><numFmt numFmtId="164" formatCode="#,##0.00"/><numFmt numFmtId="165" formatCode="yyyy-mm-dd"/></numFmts>
          <fonts count="4">
            <font><sz val="10"/><name val="Segoe UI"/></font>
            <font><b/><sz val="18"/><color rgb="FF27223F"/><name val="Segoe UI"/></font>
            <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Segoe UI"/></font>
            <font><b/><sz val="10"/><name val="Segoe UI"/></font>
          </fonts>
          <fills count="5">
            <fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF5948D6"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF0EFFA"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF5F6F8"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2"><border/><border><left style="thin"><color rgb="FFDDE1E7"/></left><right style="thin"><color rgb="FFDDE1E7"/></right><top style="thin"><color rgb="FFDDE1E7"/></top><bottom style="thin"><color rgb="FFDDE1E7"/></bottom></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="12">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2"/></xf>
            <xf numFmtId="0" fontId="3" fillId="4" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2"/></xf>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" readingOrder="2" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2" wrapText="1"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2"/></xf>
            <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="164" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
