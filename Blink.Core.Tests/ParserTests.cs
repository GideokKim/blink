using System.Text;
using Blink.Core.Parsers;

namespace Blink.Core.Tests;

public class ParserTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CreateTempFile(string ext, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink_test_{Guid.NewGuid()}{ext}");
        File.WriteAllText(path, content, encoding ?? Encoding.UTF8);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempFileWithBom(string ext, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink_test_{Guid.NewGuid()}{ext}");
        // Write UTF-8 BOM explicitly
        using var fs = new FileStream(path, FileMode.Create);
        var bom = Encoding.UTF8.GetPreamble();
        fs.Write(bom, 0, bom.Length);
        var bytes = Encoding.UTF8.GetBytes(content);
        fs.Write(bytes, 0, bytes.Length);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void TxtFile_RoutesToTextParser()
    {
        var parser = ParserRegistry.GetParser("readme.txt");
        Assert.IsType<TextParser>(parser);
    }

    [Fact]
    public void MdFile_RoutesToTextParser()
    {
        var parser = ParserRegistry.GetParser("notes.md");
        Assert.IsType<TextParser>(parser);
    }

    [Fact]
    public void UnknownExt_RoutesToFilenameOnlyParser()
    {
        var parser = ParserRegistry.GetParser("data.bin");
        Assert.IsType<FilenameOnlyParser>(parser);
    }

    [Fact]
    public void TextParser_ReadsContent()
    {
        var path = CreateTempFile(".txt", "Hello Blink");
        var parser = ParserRegistry.GetParser(path);
        Assert.Equal("Hello Blink", parser.ExtractText(path));
    }

    [Fact]
    public void MdParser_ReadsContent()
    {
        var path = CreateTempFile(".md", "# Heading\nBody text");
        var parser = ParserRegistry.GetParser(path);
        Assert.Equal("# Heading\nBody text", parser.ExtractText(path));
    }

    [Fact]
    public void FilenameOnlyParser_ReturnsEmpty()
    {
        var path = CreateTempFile(".bin", "binary data");
        var parser = ParserRegistry.GetParser(path);
        Assert.Equal(string.Empty, parser.ExtractText(path));
    }

    [Fact]
    public void TextParser_ReadsContent_NoBomLeadingChar()
    {
        var path = CreateTempFileWithBom(".txt", "BOM content here");
        var parser = ParserRegistry.GetParser(path);
        var content = parser.ExtractText(path);
        // Should NOT start with BOM char (U+FEFF)
        Assert.False(content.StartsWith('﻿'), "Content should not start with BOM character");
        Assert.Equal("BOM content here", content);
    }

    [Fact]
    public void TextParser_ReadsContent_WithHangul()
    {
        var path = CreateTempFile(".txt", "한글 텍스트");
        var parser = ParserRegistry.GetParser(path);
        Assert.Equal("한글 텍스트", parser.ExtractText(path));
    }

    [Fact]
    public void TextParser_ReadsContent_True()
    {
        var parser = new TextParser();
        Assert.True(parser.ReadsContent);
    }

    [Fact]
    public void FilenameOnlyParser_ReadsContent_False()
    {
        var parser = new FilenameOnlyParser();
        Assert.False(parser.ReadsContent);
    }

    [Fact]
    public void ParserRegistry_CaseInsensitiveExtension()
    {
        var parser = ParserRegistry.GetParser("readme.TXT");
        Assert.IsType<TextParser>(parser);
    }
}
