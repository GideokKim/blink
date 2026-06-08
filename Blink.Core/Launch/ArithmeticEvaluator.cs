using System.Globalization;

namespace Blink.Core.Launch;

/// <summary>
/// A tiny recursive-descent evaluator for the inline calculator. Supports <c>+ - * /</c>,
/// parentheses, unary minus, and decimals — the operators the prototype's expression
/// regex (<c>^[-+/*().\d\s]+$</c>) admits. Replaces the prototype's JavaScript
/// <c>Function(...)</c> eval with a safe parser (no code execution, culture-invariant).
/// </summary>
internal static class ArithmeticEvaluator
{
    /// <summary>Evaluate <paramref name="expr"/>; returns null if it is not a well-formed expression.</summary>
    public static double? Evaluate(string expr)
    {
        try
        {
            int pos = 0;
            double value = ParseExpression(expr, ref pos);
            SkipWhitespace(expr, ref pos);
            if (pos != expr.Length)
                return null; // trailing garbage
            return double.IsFinite(value) ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // expr := term (('+' | '-') term)*
    private static double ParseExpression(string s, ref int pos)
    {
        double left = ParseTerm(s, ref pos);
        while (true)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) break;
            char op = s[pos];
            if (op != '+' && op != '-') break;
            pos++;
            double right = ParseTerm(s, ref pos);
            left = op == '+' ? left + right : left - right;
        }
        return left;
    }

    // term := factor (('*' | '/') factor)*
    private static double ParseTerm(string s, ref int pos)
    {
        double left = ParseFactor(s, ref pos);
        while (true)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) break;
            char op = s[pos];
            if (op != '*' && op != '/') break;
            pos++;
            double right = ParseFactor(s, ref pos);
            left = op == '*' ? left * right : left / right; // float division, like JS
        }
        return left;
    }

    // factor := '-' factor | '+' factor | '(' expr ')' | number
    private static double ParseFactor(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) throw new FormatException();

        char c = s[pos];
        if (c == '-') { pos++; return -ParseFactor(s, ref pos); }
        if (c == '+') { pos++; return ParseFactor(s, ref pos); }
        if (c == '(')
        {
            pos++;
            double v = ParseExpression(s, ref pos);
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != ')') throw new FormatException();
            pos++;
            return v;
        }
        return ParseNumber(s, ref pos);
    }

    private static double ParseNumber(string s, ref int pos)
    {
        int start = pos;
        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.'))
            pos++;
        if (pos == start)
            throw new FormatException();
        if (!double.TryParse(s.AsSpan(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new FormatException();
        return v;
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
    }
}
