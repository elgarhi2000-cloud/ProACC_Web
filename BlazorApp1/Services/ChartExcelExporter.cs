using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
namespace BlazorApp1.Services;

public sealed record ChartExportRow(int Level, string Number, string Name, string ParentNumber, string Path);
public static class ChartExcelExporter
{
    private static readonly XNamespace Ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static byte[] Create(IReadOnlyList<ChartExportRow> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Write(string path, string text) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text); }
            Write("[Content_Types].xml", ContentTypesXml);
            Write("_rels/.rels", RootRelationshipsXml);
            Write("xl/workbook.xml", WorkbookXml);
            Write("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
            Write("xl/styles.xml", StylesXml);
            XElement Cell(string address, string value, int style) => new(Ss + "c", new XAttribute("r", address), new XAttribute("t", "inlineStr"), new XAttribute("s", style), new XElement(Ss + "is", new XElement(Ss + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
            var data = new XElement(Ss + "sheetData");
            var headers = new[] { "المستوى", "رقم الحساب", "اسم الحساب", "رقم الحساب الرئيسي", "المسار الكامل" };
            data.Add(new XElement(Ss + "row", new XAttribute("r", 1), headers.Select((h, i) => Cell($"{(char)('A' + i)}1", h, 5))));
            int index = 2;
            foreach (var row in rows)
            {
                var values = new[] { row.Level.ToString(), row.Number, row.Name, row.ParentNumber, row.Path };
                data.Add(new XElement(Ss + "row", new XAttribute("r", index), new XAttribute("outlineLevel", row.Level - 1),
                    values.Select((v, i) => Cell($"{(char)('A' + i)}{index}", v, row.Level < 6 ? 8 : 6))));
                index++;
            }
            var sheet = new XElement(Ss + "worksheet",
                new XElement(Ss + "sheetViews", new XElement(Ss + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1),
                    new XElement(Ss + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen"), new XAttribute("activePane", "bottomLeft")))),
                new XElement(Ss + "cols", new[] { 12, 22, 40, 22, 100 }.Select((w, i) => new XElement(Ss + "col", new XAttribute("min", i+1), new XAttribute("max", i+1), new XAttribute("width", w), new XAttribute("customWidth", 1)))),
                data, new XElement(Ss + "autoFilter", new XAttribute("ref", $"A1:E{index-1}")));
            Write("xl/worksheets/sheet1.xml", new XDocument(sheet).ToString());
        }
        return stream.ToArray();
    }
    private const string ContentTypesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""";
    private const string RootRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";
    private const string WorkbookXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><bookViews><workbookView/></bookViews><sheets><sheet name="دليل الحسابات" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="191029" calcMode="auto" fullCalcOnLoad="1"/></workbook>""";
    private const string WorkbookRelationshipsXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";
    private const string StylesXml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00;[Red](#,##0.00);-"/></numFmts><fonts count="4"><font><sz val="10"/><name val="Segoe UI"/></font><font><b/><sz val="18"/><name val="Segoe UI"/></font><font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Segoe UI"/></font><font><b/><sz val="10"/><name val="Segoe UI"/></font></fonts><fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF5948D6"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF0EFFA"/></patternFill></fill></fills><borders count="2"><border/><border><left style="thin"><color rgb="FFDDE1E7"/></left><right style="thin"><color rgb="FFDDE1E7"/></right><top style="thin"><color rgb="FFDDE1E7"/></top><bottom style="thin"><color rgb="FFDDE1E7"/></bottom></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="10"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" readingOrder="2"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" vertical="center" readingOrder="2"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1"><alignment horizontal="right"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="right" readingOrder="2"/></xf><xf numFmtId="164" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1"><alignment horizontal="right"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";
}
