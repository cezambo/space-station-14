using System.Globalization;

namespace Cognition.Core.Decision;

public sealed class IncludeConditionException(string message) : Exception(message);

/// <summary>
/// The <c>include_when</c> language of <c>jev/*.yaml</c>: identifiers, integers, <c>likely(name)</c>,
/// <c>== != &gt;= &lt;= &gt; &lt;</c>, <c>+ -</c>, <c>&amp;&amp; ||</c> and parentheses. Values are booleans, integers or
/// strings; every identifier must be a known variable (a typo is an error, not <c>false</c>).
/// <c>&amp;&amp;</c> and <c>||</c> short-circuit, so <c>likely()</c> is only asked when it matters.
/// </summary>
public sealed class IncludeCondition
{
    private readonly Node _root;

    private IncludeCondition(string text, Node root)
    {
        Text = text;
        _root = root;
    }

    public string Text { get; }

    /// <summary>Identifiers the expression reads, excluding <c>likely</c> arguments.</summary>
    public IReadOnlySet<string> Variables => _root.Variables().ToHashSet(StringComparer.Ordinal);

    public static IncludeCondition Parse(string text)
    {
        var tokens = Tokenize(text);
        var pos = 0;
        var root = Or(tokens, ref pos, text);
        if (pos != tokens.Count)
            throw new IncludeConditionException($"include_when '{text}': unexpected '{tokens[pos]}'");
        return new IncludeCondition(text, root);
    }

    public bool Evaluate(IReadOnlyDictionary<string, object> variables, Func<string, bool> likely) =>
        _root.Eval(variables, likely, Text) is bool b
            ? b
            : throw new IncludeConditionException($"include_when '{Text}': does not evaluate to true/false");

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                tokens.Add(text[start..i]);
            }
            else if (char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                tokens.Add(text[start..i]);
            }
            else if (i + 1 < text.Length && text.Substring(i, 2) is "&&" or "||" or "==" or "!=" or ">=" or "<=")
            {
                tokens.Add(text.Substring(i, 2));
                i += 2;
            }
            else if (c is '(' or ')' or '>' or '<' or '+' or '-')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else
            {
                throw new IncludeConditionException($"include_when '{text}': unexpected character '{c}'");
            }
        }

        return tokens;
    }

    private static string? Peek(List<string> t, int pos) => pos < t.Count ? t[pos] : null;

    private static void Expect(List<string> t, ref int pos, string token, string text)
    {
        if (Peek(t, pos) != token)
            throw new IncludeConditionException($"include_when '{text}': expected '{token}'");
        pos++;
    }

    private static Node Or(List<string> t, ref int pos, string text)
    {
        var left = And(t, ref pos, text);
        while (Peek(t, pos) == "||")
        {
            pos++;
            left = new Binary("||", left, And(t, ref pos, text));
        }

        return left;
    }

    private static Node And(List<string> t, ref int pos, string text)
    {
        var left = Compare(t, ref pos, text);
        while (Peek(t, pos) == "&&")
        {
            pos++;
            left = new Binary("&&", left, Compare(t, ref pos, text));
        }

        return left;
    }

    private static Node Compare(List<string> t, ref int pos, string text)
    {
        var left = Sum(t, ref pos, text);
        if (Peek(t, pos) is "==" or "!=" or ">=" or "<=" or ">" or "<")
        {
            var op = t[pos++];
            return new Binary(op, left, Sum(t, ref pos, text));
        }

        return left;
    }

    private static Node Sum(List<string> t, ref int pos, string text)
    {
        var left = Atom(t, ref pos, text);
        while (Peek(t, pos) is "+" or "-")
        {
            var op = t[pos++];
            left = new Binary(op, left, Atom(t, ref pos, text));
        }

        return left;
    }

    private static Node Atom(List<string> t, ref int pos, string text)
    {
        var token = Peek(t, pos) ?? throw new IncludeConditionException($"include_when '{text}': ends too early");
        pos++;
        if (token == "(")
        {
            var inner = Or(t, ref pos, text);
            Expect(t, ref pos, ")", text);
            return inner;
        }

        if (char.IsDigit(token[0]))
            return new Number(int.Parse(token, CultureInfo.InvariantCulture));
        if (!(char.IsLetter(token[0]) || token[0] == '_'))
            throw new IncludeConditionException($"include_when '{text}': unexpected '{token}'");
        if (token == "likely")
        {
            Expect(t, ref pos, "(", text);
            var arg = Peek(t, pos);
            if (arg is null || !(char.IsLetter(arg[0]) || arg[0] == '_'))
                throw new IncludeConditionException($"include_when '{text}': likely() takes a name");
            pos++;
            Expect(t, ref pos, ")", text);
            return new Likely(arg);
        }

        return new Variable(token);
    }

    private abstract record Node
    {
        public abstract object Eval(IReadOnlyDictionary<string, object> v, Func<string, bool> likely, string text);

        public abstract IEnumerable<string> Variables();
    }

    private sealed record Number(int Value) : Node
    {
        public override object Eval(IReadOnlyDictionary<string, object> v, Func<string, bool> likely, string text) => Value;

        public override IEnumerable<string> Variables() => [];
    }

    private sealed record Variable(string Name) : Node
    {
        public override object Eval(IReadOnlyDictionary<string, object> v, Func<string, bool> likely, string text) =>
            v.TryGetValue(Name, out var value)
                ? value
                : throw new IncludeConditionException($"include_when '{text}': unknown name '{Name}'");

        public override IEnumerable<string> Variables() => [Name];
    }

    private sealed record Likely(string Name) : Node
    {
        public override object Eval(IReadOnlyDictionary<string, object> v, Func<string, bool> likely, string text) => likely(Name);

        public override IEnumerable<string> Variables() => [];
    }

    private sealed record Binary(string Op, Node Left, Node Right) : Node
    {
        public override object Eval(IReadOnlyDictionary<string, object> v, Func<string, bool> likely, string text)
        {
            if (Op is "&&" or "||")
            {
                var l = Bool(Left.Eval(v, likely, text), text);
                if (Op == "&&" ? !l : l)
                    return l;
                return Bool(Right.Eval(v, likely, text), text);
            }

            var a = Left.Eval(v, likely, text);
            var b = Right.Eval(v, likely, text);
            return Op switch
            {
                "==" => Equals(a, b),
                "!=" => !Equals(a, b),
                ">=" => Int(a, text) >= Int(b, text),
                "<=" => Int(a, text) <= Int(b, text),
                ">" => Int(a, text) > Int(b, text),
                "<" => Int(a, text) < Int(b, text),
                "+" => Int(a, text) + Int(b, text),
                _ => Int(a, text) - Int(b, text),
            };
        }

        public override IEnumerable<string> Variables() => Left.Variables().Concat(Right.Variables());

        private static bool Bool(object o, string text) =>
            o as bool? ?? throw new IncludeConditionException($"include_when '{text}': '{o}' is not true/false");

        private static int Int(object o, string text) =>
            o as int? ?? throw new IncludeConditionException($"include_when '{text}': '{o}' is not a number");
    }
}
