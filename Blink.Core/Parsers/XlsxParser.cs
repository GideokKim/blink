using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Blink.Core.Parsers;

/// <summary>
/// Extracts cell text from <c>.xlsx</c> workbooks (Open XML). Shared-string cells are
/// resolved against the workbook's shared string table; inline/number/date cells use
/// their stored value. Cell texts are space-joined — sufficient for full-text search
/// over tabular data (e.g. vendor contact sheets). Replaces the Python prototype's openpyxl.
/// </summary>
public sealed class XlsxParser : IParser
{
    public string Name => "XlsxParser";
    public string[] Extensions => [".xlsx"];
    public bool ReadsContent => true;
    public long? MaxParseSize => 25L * 1024 * 1024;

    // A workbook may stay under the 25MB size cap yet still explode on cell count (huge sheets,
    // conditional-formatting bloat). Walk rows so we can bail out with a partial body once we hit
    // either ceiling instead of touching every cell (O(cells)).
    private const int MaxCells = 100_000;
    private const int MaxRows = 10_000;

    public string ExtractText(string path) => ExtractText(path, MaxCells, MaxRows);

    public string ExtractText(string path, int maxCells, int maxRows)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null)
            return string.Empty;

        var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();
        int cells = 0, rows = 0;
        bool truncated = false;

        foreach (var wsPart in workbookPart.WorksheetParts)
        {
            if (truncated) break;
            if (wsPart.Worksheet is null) continue;

            foreach (var row in wsPart.Worksheet.Descendants<Row>())
            {
                if (++rows > maxRows) { truncated = true; break; }
                foreach (var cell in row.Elements<Cell>())
                {
                    var text = CellText(cell, sst);
                    if (text.Length == 0) continue;          // sparse/empty cells aren't counted
                    sb.Append(text); sb.Append(' ');
                    if (++cells >= maxCells) { truncated = true; break; }
                }
                if (truncated) break;
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string CellText(Cell cell, SharedStringTable? sst)
    {
        if (cell.CellValue is null)
            return cell.InlineString?.InnerText ?? string.Empty;

        var raw = cell.CellValue.InnerText;
        if (cell.DataType?.Value == CellValues.SharedString
            && sst is not null
            && int.TryParse(raw, out var idx)
            && idx >= 0 && idx < sst.ChildElements.Count)
        {
            return sst.ChildElements[idx].InnerText;
        }
        return raw;
    }
}
