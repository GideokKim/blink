using System.Text;
using Blink.Core.Tokenization;

namespace Blink.Core.Tests;

public class NgramTokenizerTests
{
    [Fact]
    public void Hangul_FourChars_Produces2gramsAnd3grams()
    {
        // 한글검색 → 2-grams: 한글, 글검, 검색  3-grams: 한글검, 글검색
        var tokens = NgramTokenizer.Tokens("한글검색");

        Assert.Contains("한글", tokens);
        Assert.Contains("글검", tokens);
        Assert.Contains("검색", tokens);
        Assert.Contains("한글검", tokens);
        Assert.Contains("글검색", tokens);

        // No 4-gram
        Assert.DoesNotContain("한글검색", tokens);
    }

    [Fact]
    public void Ascii_ReturnsWholeWords()
    {
        var tokens = NgramTokenizer.Tokens("Hello World");

        Assert.Contains("hello", tokens);
        Assert.Contains("world", tokens);
    }

    [Fact]
    public void Mixed_AsciiAndHangul_BothTokenized()
    {
        // "abc한글" → abc + 한글-grams
        var tokens = NgramTokenizer.Tokens("abc한글");

        Assert.Contains("abc", tokens);
        Assert.Contains("한글", tokens);
    }

    [Fact]
    public void NfcNormalization_IdempotentForNfdInput()
    {
        // NFD-composed input and NFC form yield the same tokens
        var nfc = "한글검색";
        var nfd = nfc.Normalize(NormalizationForm.FormD);

        var tokensNfc = NgramTokenizer.Tokens(nfc);
        var tokensNfd = NgramTokenizer.Tokens(nfd);

        Assert.Equal(tokensNfc.OrderBy(x => x).ToList(),
                     tokensNfd.OrderBy(x => x).ToList());
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Empty(NgramTokenizer.Tokens(""));
    }

    [Fact]
    public void WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Empty(NgramTokenizer.Tokens("   \t\n"));
    }

    [Fact]
    public void Tokenize_ReturnsSpaceJoined()
    {
        var joined = NgramTokenizer.Tokenize("hello world");
        var tokens = joined.Split(' ');
        Assert.Contains("hello", tokens);
        Assert.Contains("world", tokens);
    }

    [Fact]
    public void Tokens_AreDistinct()
    {
        // Repeated input should not produce duplicates
        var tokens = NgramTokenizer.Tokens("한한한");
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void Tokens_AreSorted_Ordinal()
    {
        var tokens = NgramTokenizer.Tokens("hello world abc 한글검색");
        var sorted = tokens.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, tokens.ToList());
    }

    [Fact]
    public void Digits_TreatedAsAsciiRun()
    {
        var tokens = NgramTokenizer.Tokens("abc123");
        Assert.Contains("abc123", tokens);
    }

    [Fact]
    public void SingleHangulChar_EmittedAsToken()
    {
        var tokens = NgramTokenizer.Tokens("한");
        Assert.Contains("한", tokens);
    }

    [Fact]
    public void ThreeHangulChars_Produces2gramsAnd3gram()
    {
        // 한글검 → 2-grams: 한글, 글검   3-gram: 한글검
        var tokens = NgramTokenizer.Tokens("한글검");

        Assert.Contains("한글", tokens);
        Assert.Contains("글검", tokens);
        Assert.Contains("한글검", tokens);
    }
}
