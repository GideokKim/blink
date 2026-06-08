using System.IO.Compression;
using System.Text;
using Blink.Core.Parsers;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Blink.Core.Tests;

/// <summary>
/// Content-extraction tests for the rich format parsers (xlsx/pdf/docx/pptx/hwpx/rtf).
/// Fixtures are authored on the fly so no binary blobs live in the repo.
/// </summary>
public sealed class RichParserTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string Temp(string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink_rich_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    // ---- Registry routing for every new extension ----
    [Theory]
    [InlineData("book.xlsx", typeof(XlsxParser))]
    [InlineData("doc.pdf", typeof(PdfParser))]
    [InlineData("memo.docx", typeof(DocxParser))]
    [InlineData("deck.pptx", typeof(PptxParser))]
    [InlineData("hancom.hwpx", typeof(HwpxParser))]
    [InlineData("note.rtf", typeof(RtfParser))]
    public void Registry_RoutesByExtension(string name, Type parserType)
        => Assert.IsType(parserType, ParserRegistry.GetParser(name));

    // ---- XLSX (shared strings — the common real-world case) ----
    [Fact]
    public void Xlsx_ExtractsSharedStringCells()
    {
        var path = Temp(".xlsx");
        using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();

            var sst = wbPart.AddNewPart<SharedStringTablePart>();
            sst.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text("업체명")),
                new SharedStringItem(new Text("Acme전자")));

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var row = new Row();
            row.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue("0") });
            row.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue("1") });
            sheetData.Append(row);
            wsPart.Worksheet = new Worksheet(sheetData);

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Sheet1" });
            wbPart.Workbook.Save();
        }

        var text = ParserRegistry.GetParser(path).ExtractText(path);
        Assert.Contains("업체명", text);
        Assert.Contains("Acme전자", text);
    }

    // ---- DOCX ----
    [Fact]
    public void Docx_ExtractsParagraphText()
    {
        var path = Temp(".docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("계약서 초안"))),
                new W.Paragraph(new W.Run(new W.Text("second clause")))));
            main.Document.Save();
        }

        var text = ParserRegistry.GetParser(path).ExtractText(path);
        Assert.Contains("계약서 초안", text);
        Assert.Contains("second clause", text);
    }

    // ---- PPTX ----
    [Fact]
    public void Pptx_ExtractsSlideText()
    {
        var path = Temp(".pptx");
        using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
        {
            var pPart = doc.AddPresentationPart();
            pPart.Presentation = new P.Presentation();

            var slidePart = pPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(),
                        new P.Shape(
                            new P.NonVisualShapeProperties(
                                new P.NonVisualDrawingProperties { Id = 2U, Name = "Title" },
                                new P.NonVisualShapeDrawingProperties(),
                                new P.ApplicationNonVisualDrawingProperties()),
                            new P.ShapeProperties(),
                            new P.TextBody(
                                new A.BodyProperties(),
                                new A.ListStyle(),
                                new A.Paragraph(new A.Run(new A.RunProperties(), new A.Text("발표 자료 제목"))))))));
            slidePart.Slide.Save();
        }

        var text = ParserRegistry.GetParser(path).ExtractText(path);
        Assert.Contains("발표 자료 제목", text);
    }

    // ---- PDF (hand-built minimal text-layer PDF; ASCII) ----
    [Fact]
    public void Pdf_ExtractsTextLayer()
    {
        var path = Temp(".pdf");
        File.WriteAllBytes(path, BuildSimplePdf("Hello PDF Blink"));

        var text = ParserRegistry.GetParser(path).ExtractText(path);
        Assert.Contains("Hello", text);
        Assert.Contains("Blink", text);
    }

    private static byte[] BuildSimplePdf(string asciiText)
    {
        var stream = $"BT /F1 24 Tf 72 720 Td ({asciiText}) Tj ET";
        var objs = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>",
            $"<</Length {stream.Length}>>\nstream\n{stream}\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new int[objs.Length];
        for (int n = 0; n < objs.Length; n++)
        {
            offsets[n] = sb.Length; // all-ASCII content → char index == byte offset
            sb.Append($"{n + 1} 0 obj\n{objs[n]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append("xref\n").Append($"0 {objs.Length + 1}\n").Append("0000000000 65535 f \n");
        foreach (var off in offsets)
            sb.Append($"{off:D10} 00000 n \n");
        sb.Append($"trailer<</Size {objs.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ---- HWPX (OpenXML-style zip; text under Contents/*.xml) ----
    [Fact]
    public void Hwpx_ExtractsContentsXmlText()
    {
        var path = Temp(".hwpx");
        using (var fs = new FileStream(path, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Contents/section0.xml");
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\"?><hml><p><run><t>한컴 문서 본문</t></run></p></hml>");
        }

        var text = ParserRegistry.GetParser(path).ExtractText(path);
        Assert.Contains("한컴 문서 본문", text);
    }

    // ---- RTF (control-word stripping, incl. \u Unicode for Korean) ----
    [Fact]
    public void Rtf_StripsControlWords_KeepsText()
    {
        // RTF \uN uses signed 16-bit code points: '한'(U+D55C=54620)→-10916, '글'(U+AE00=44544)→-20992.
        // The '?' after each is the ANSI fallback char (\uc1 default), which must be dropped.
        const string rtf = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0 Hello \u-10916?\u-20992? world\par done}";
        var stripped = RtfParser.Strip(rtf);
        Assert.Contains("Hello", stripped);
        Assert.Contains("world", stripped);
        Assert.Contains("한글", stripped);
        Assert.DoesNotContain("fonttbl", stripped); // destination group dropped
        Assert.DoesNotContain("rtf1", stripped);
    }

    [Fact]
    public void Rtf_ExtractFromFile()
    {
        var path = Temp(".rtf");
        File.WriteAllText(path, @"{\rtf1\ansi plain 문서 text\par}", new UTF8Encoding(false));
        var text = ParserRegistry.GetParser(path).ExtractText(path);
        Assert.Contains("plain", text);
        Assert.Contains("text", text);
    }
}
