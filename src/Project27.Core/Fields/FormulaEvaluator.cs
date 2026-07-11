using System.Globalization;

namespace Project27.Core.Fields;

/// <summary>
/// The formula language for custom fields (docs/spec/09-views-fields.md §9b —
/// a clean-room subset of MSP's, deviation #26): `[Field]` references, decimal
/// and string literals, arithmetic, comparisons, and/or/not,
/// IIf/Abs/Min/Max/Round, Now(), StatusDate(). Values are decimal / string /
/// bool / DateTime / null; `+` concatenates when either side is a string.
/// </summary>
public abstract record FormulaNode
{
    public sealed record Literal(object? Value) : FormulaNode;

    /// <summary>A duration literal ("2d", "4h") — evaluates to working minutes via project time settings.</summary>
    public sealed record DurationLiteral(Time.Duration Value) : FormulaNode;

    public sealed record FieldRef(string Key) : FormulaNode;

    public sealed record Unary(string Operator, FormulaNode Operand) : FormulaNode;

    public sealed record Binary(string Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

    public sealed record Invocation(string Function, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;
}

public static class FormulaEvaluator
{
    private const int MaxDepth = 16;

    public static FormulaNode Parse(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var parser = new Parser(source);
        var node = parser.ParseExpression();
        parser.ExpectEnd();
        return node;
    }

    // Formula fields re-enter through field accessors, so the cycle guard must
    // survive accessor boundaries: a thread-local depth counter.
    [ThreadStatic]
    private static int _activeEvaluations;

    public static object? Evaluate(FormulaNode node, ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(task);
        if (_activeEvaluations > MaxDepth)
        {
            throw new InvalidOperationException("Formula evaluation exceeds the depth limit (circular field references?).");
        }

        _activeEvaluations++;
        try
        {
            return Evaluate(node, task, 0);
        }
        finally
        {
            _activeEvaluations--;
        }
    }

    private static object? Evaluate(FormulaNode node, ProjectTask task, int depth)
    {
        if (depth > 64)
        {
            throw new InvalidOperationException("The formula is nested too deeply.");
        }

        switch (node)
        {
            case FormulaNode.Literal literal:
                return literal.Value;
            case FormulaNode.DurationLiteral duration:
                return duration.Value.ToMinutes(task.Project.TimeSettings);
            case FormulaNode.FieldRef fieldRef:
            {
                var field = FieldCatalog.Resolve(task.Project, fieldRef.Key);
                return field.Accessor(task);
            }

            case FormulaNode.Unary unary:
            {
                var operand = Evaluate(unary.Operand, task, depth + 1);
                return unary.Operator switch
                {
                    "-" => -ToNumber(operand),
                    "not" => !ToBool(operand),
                    _ => throw new InvalidOperationException($"Unknown unary operator '{unary.Operator}'."),
                };
            }

            case FormulaNode.Binary binary:
                return EvaluateBinary(binary, task, depth);
            case FormulaNode.Invocation call:
                return EvaluateCall(call, task, depth);
            default:
                throw new InvalidOperationException($"Unknown formula node {node.GetType().Name}.");
        }
    }

    private static object? EvaluateBinary(FormulaNode.Binary binary, ProjectTask task, int depth)
    {
        // Short-circuit logic operators.
        if (binary.Operator is "and" or "or")
        {
            var leftFlag = ToBool(Evaluate(binary.Left, task, depth + 1));
            return binary.Operator == "and"
                ? leftFlag && ToBool(Evaluate(binary.Right, task, depth + 1))
                : leftFlag || ToBool(Evaluate(binary.Right, task, depth + 1));
        }

        var left = Evaluate(binary.Left, task, depth + 1);
        var right = Evaluate(binary.Right, task, depth + 1);
        if (binary.Operator == "+" && (left is string || right is string))
        {
            return ToText(left) + ToText(right);
        }

        if (binary.Operator is "+" or "-" or "*" or "/")
        {
            var a = ToNumber(left);
            var b = ToNumber(right);
            return binary.Operator switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                _ => b == 0 ? throw new InvalidOperationException("Division by zero in formula.") : a / b,
            };
        }

        var order = CompareValues(left, right);
        return binary.Operator switch
        {
            "=" => order == 0,
            "<>" => order != 0,
            ">" => order > 0,
            ">=" => order >= 0,
            "<" => order < 0,
            "<=" => order <= 0,
            _ => throw new InvalidOperationException($"Unknown operator '{binary.Operator}'."),
        };
    }

    private static object? EvaluateCall(FormulaNode.Invocation call, ProjectTask task, int depth)
    {
        object? Arg(int index) => Evaluate(call.Arguments[index], task, depth + 1);
        void Expect(int count)
        {
            if (call.Arguments.Count != count)
            {
                throw new InvalidOperationException($"{call.Function} expects {count} argument(s), got {call.Arguments.Count}.");
            }
        }

        switch (call.Function.ToUpperInvariant())
        {
            case "IIF":
                Expect(3);
                return ToBool(Arg(0)) ? Arg(1) : Arg(2);
            case "ABS":
                Expect(1);
                return Math.Abs(ToNumber(Arg(0)));
            case "MIN":
                Expect(2);
                return Math.Min(ToNumber(Arg(0)), ToNumber(Arg(1)));
            case "MAX":
                Expect(2);
                return Math.Max(ToNumber(Arg(0)), ToNumber(Arg(1)));
            case "ROUND":
            {
                if (call.Arguments.Count is not (1 or 2))
                {
                    throw new InvalidOperationException($"Round expects 1 or 2 arguments, got {call.Arguments.Count}.");
                }

                var digits = call.Arguments.Count == 2 ? (int)ToNumber(Arg(1)) : 0;
                return Math.Round(ToNumber(Arg(0)), digits, MidpointRounding.AwayFromZero);
            }

            case "NOW":
                Expect(0);
                return DateTime.Now;
            case "STATUSDATE":
                Expect(0);
                return task.Project.StatusDate
                    ?? throw new InvalidOperationException("StatusDate() needs the project status date to be set.");
            default:
                throw new InvalidOperationException($"Unknown function '{call.Function}'.");
        }
    }

    private static decimal ToNumber(object? value) => value switch
    {
        null => 0m,
        decimal number => number,
        int integer => integer,
        bool flag => flag ? 1m : 0m,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new InvalidOperationException($"'{value}' is not a number."),
    };

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        decimal number => number != 0m,
        int integer => integer != 0,
        _ => throw new InvalidOperationException($"'{value}' is not a condition."),
    };

    private static string ToText(object? value) => value switch
    {
        null => "",
        decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        _ => value.ToString() ?? "",
    };

    private static int CompareValues(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null ? 0 : left is null ? -1 : 1;
        }

        if (left is string || right is string)
        {
            return string.Compare(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
        }

        if (left is DateTime leftDate && right is DateTime rightDate)
        {
            return DateTime.Compare(leftDate, rightDate);
        }

        return ToNumber(left).CompareTo(ToNumber(right));
    }

    // ---------------------------------------------------------------- parser

    private sealed class Parser(string source)
    {
        private int _position;

        public FormulaNode ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_position < source.Length)
            {
                throw new FormatException($"Unexpected '{source[_position..]}' in formula.");
            }
        }

        private FormulaNode ParseOr()
        {
            var left = ParseAnd();
            while (TakeKeyword("or"))
            {
                left = new FormulaNode.Binary("or", left, ParseAnd());
            }

            return left;
        }

        private FormulaNode ParseAnd()
        {
            var left = ParseComparison();
            while (TakeKeyword("and"))
            {
                left = new FormulaNode.Binary("and", left, ParseComparison());
            }

            return left;
        }

        private FormulaNode ParseComparison()
        {
            var left = ParseAdditive();
            foreach (var op in (string[])["<>", "<=", ">=", "=", "<", ">"])
            {
                if (TakeSymbol(op))
                {
                    return new FormulaNode.Binary(op, left, ParseAdditive());
                }
            }

            return left;
        }

        private FormulaNode ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (TakeSymbol("+"))
                {
                    left = new FormulaNode.Binary("+", left, ParseMultiplicative());
                }
                else if (TakeSymbol("-"))
                {
                    left = new FormulaNode.Binary("-", left, ParseMultiplicative());
                }
                else
                {
                    return left;
                }
            }
        }

        private FormulaNode ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                if (TakeSymbol("*"))
                {
                    left = new FormulaNode.Binary("*", left, ParseUnary());
                }
                else if (TakeSymbol("/"))
                {
                    left = new FormulaNode.Binary("/", left, ParseUnary());
                }
                else
                {
                    return left;
                }
            }
        }

        private FormulaNode ParseUnary()
        {
            if (TakeSymbol("-"))
            {
                return new FormulaNode.Unary("-", ParseUnary());
            }

            if (TakeKeyword("not"))
            {
                return new FormulaNode.Unary("not", ParseUnary());
            }

            return ParsePrimary();
        }

        private FormulaNode ParsePrimary()
        {
            SkipWhitespace();
            if (_position >= source.Length)
            {
                throw new FormatException("The formula ends unexpectedly.");
            }

            var current = source[_position];
            if (current == '(')
            {
                _position++;
                var inner = ParseOr();
                Require(')');
                return inner;
            }

            if (current == '[')
            {
                var end = source.IndexOf(']', _position + 1);
                if (end < 0)
                {
                    throw new FormatException("Unterminated [field] reference.");
                }

                var key = source[(_position + 1)..end].Trim();
                _position = end + 1;
                return new FormulaNode.FieldRef(key);
            }

            if (current is '"' or '\'')
            {
                var end = source.IndexOf(current, _position + 1);
                if (end < 0)
                {
                    throw new FormatException("Unterminated string in formula.");
                }

                var text = source[(_position + 1)..end];
                _position = end + 1;
                return new FormulaNode.Literal(text);
            }

            if (char.IsDigit(current) || current == '.')
            {
                var start = _position;
                while (_position < source.Length && (char.IsDigit(source[_position]) || source[_position] == '.'))
                {
                    _position++;
                }

                // A unit suffix turns the number into a duration literal ("2d", "4eh").
                var suffixStart = _position;
                while (_position < source.Length && char.IsLetter(source[_position]))
                {
                    _position++;
                }

                if (_position > suffixStart)
                {
                    var text = source[start.._position];
                    if (Time.Duration.TryParse(text, out var duration))
                    {
                        return new FormulaNode.DurationLiteral(duration);
                    }

                    throw new FormatException($"'{text}' is neither a number nor a duration.");
                }

                return new FormulaNode.Literal(decimal.Parse(source[start.._position], CultureInfo.InvariantCulture));
            }

            if (char.IsLetter(current))
            {
                var start = _position;
                while (_position < source.Length && (char.IsLetterOrDigit(source[_position]) || source[_position] == '_'))
                {
                    _position++;
                }

                var word = source[start.._position];
                if (word.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return new FormulaNode.Literal(true);
                }

                if (word.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return new FormulaNode.Literal(false);
                }

                SkipWhitespace();
                if (_position < source.Length && source[_position] == '(')
                {
                    _position++;
                    var arguments = new List<FormulaNode>();
                    SkipWhitespace();
                    if (_position < source.Length && source[_position] != ')')
                    {
                        arguments.Add(ParseOr());
                        while (TakeSymbol(","))
                        {
                            arguments.Add(ParseOr());
                        }
                    }

                    Require(')');
                    return new FormulaNode.Invocation(word, arguments);
                }

                throw new FormatException($"Unknown identifier '{word}'; field references use [brackets].");
            }

            throw new FormatException($"Unexpected '{current}' in formula.");
        }

        private void Require(char expected)
        {
            SkipWhitespace();
            if (_position >= source.Length || source[_position] != expected)
            {
                throw new FormatException($"Expected '{expected}' in formula.");
            }

            _position++;
        }

        private bool TakeSymbol(string symbol)
        {
            SkipWhitespace();
            if (_position + symbol.Length <= source.Length
                && source.AsSpan(_position, symbol.Length).SequenceEqual(symbol))
            {
                // Don't split "<=" into "<" + "=".
                if (symbol is "=" or "<" or ">" && _position + 1 < source.Length && source[_position + 1] is '=' or '>')
                {
                    return false;
                }

                _position += symbol.Length;
                return true;
            }

            return false;
        }

        private bool TakeKeyword(string keyword)
        {
            SkipWhitespace();
            if (_position + keyword.Length <= source.Length
                && source.AsSpan(_position, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)
                && (_position + keyword.Length == source.Length || !char.IsLetterOrDigit(source[_position + keyword.Length])))
            {
                _position += keyword.Length;
                return true;
            }

            return false;
        }

        private void SkipWhitespace()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position]))
            {
                _position++;
            }
        }
    }
}
