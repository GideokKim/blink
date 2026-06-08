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

    public string ExtractText(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null)
            return string.Empty;

        var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();

        foreach (var wsPart in workbookPart.WorksheetParts)
        {
            if (wsPart.Worksheet is null)
                continue;
            foreach (var cell in wsPart.Worksheet.Descendants<Cell>())
            {
                var text = CellText(cell, sst);
                if (text.Length > 0)
                {
                    sb.Append(text);
                    sb.Append(' ');
                }
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
