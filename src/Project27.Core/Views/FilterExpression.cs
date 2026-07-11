using Project27.Core.Fields;

namespace Project27.Core.Views;

public enum FilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,

    /// <summary>Case-insensitive substring match (`~`), text fields only.</summary>
    Contains,
}

/// <summary>A parsed filter expression node.</summary>
public abstract record FilterNode
{
    public sealed record Comparison(FieldDefinition Field, FilterOperator Operator, object Literal) : FilterNode;

    public sealed record AllOf(FilterNode Left, FilterNode Right) : FilterNode;

    public sealed record AnyOf(FilterNode Left, FilterNode Right) : FilterNode;

    public sealed record Negation(FilterNode Operand) : FilterNode;

    public bool Matches(ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return this switch
        {
            Comparison comparison => Evaluate(comparison, task),
            AllOf allOf => allOf.Left.Matches(task) && allOf.Right.Matches(task),
            AnyOf anyOf => anyOf.Left.Matches(task) || anyOf.Right.Matches(task),
            Negation negation => !negation.Operand.Matches(task),
            _ => throw new InvalidOperationException($"Unknown filter node {GetType().Name}."),
        };
    }

    private static bool Evaluate(Comparison comparison, ProjectTask task)
    {
        var value = comparison.Field.Accessor(task);
        if (comparison.Operator == FilterOperator.Contains)
        {
            return value?.ToString()?.Contains((string)comparison.Literal, StringComparison.OrdinalIgnoreCase) == true;
        }

        if (value is null)
        {
            // Absent values match nothing except explicit inequality.
            return comparison.Operator == FilterOperator.NotEquals;
        }

        var order = FieldCatalog.Compare(comparison.Field.Kind, value, comparison.Literal);
        return comparison.Operator switch
        {
            FilterOperator.Equals => order == 0,
            FilterOperator.NotEquals => order != 0,
            FilterOperator.GreaterThan => order > 0,
            FilterOperator.GreaterOrEqual => order >= 0,
            FilterOperator.LessThan => order < 0,
            FilterOperator.LessOrEqual => order <= 0,
            _ => throw new InvalidOperationException($"Unknown operator {comparison.Operator}."),
        };
    }
}

/// <summary>
/// Recursive-descent parser for filter expressions
/// (docs/spec/09-views-fields.md): `critical = true and (cost > 1000 or name ~ "api")`.
/// </summary>
public static class FilterParser
{
    public static FilterNode Parse(Project project, string expression)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var tokens = Tokenize(expression);
        var position = 0;
        var node = ParseOr(project, tokens, ref position);
        if (position != tokens.Count)
        {
            throw new FormatException($"Unexpected '{tokens[position]}' in filter.");
        }

        return node;
    }

    private static FilterNode ParseOr(Project project, List<string> tokens, ref int position)
    {
        var left = ParseAnd(project, tokens, ref position);
        while (position < tokens.Count && Is(tokens[position], "or"))
        {
            position++;
            left = new FilterNode.AnyOf(left, ParseAnd(project, tokens, ref position));
        }

        return left;
    }

    private static FilterNode ParseAnd(Project project, List<string> tokens, ref int position)
    {
        var left = ParseUnary(project, tokens, ref position);
        while (position < tokens.Count && Is(tokens[position], "and"))
        {
            position++;
            left = new FilterNode.AllOf(left, ParseUnary(project, tokens, ref position));
        }

        return left;
    }

    private static FilterNode ParseUnary(Project project, List<string> tokens, ref int position)
    {
        if (position >= tokens.Count)
        {
            throw new FormatException("The filter ends unexpectedly.");
        }

        if (Is(tokens[position], "not"))
        {
            position++;
            return new FilterNode.Negation(ParseUnary(project, tokens, ref position));
        }

        if (tokens[position] == "(")
        {
            position++;
            var inner = ParseOr(project, tokens, ref position);
            if (position >= tokens.Count || tokens[position] != ")")
            {
                throw new FormatException("Missing ')' in filter.");
            }

            position++;
            return inner;
        }

        return ParseComparison(project, tokens, ref position);
    }

    private static FilterNode.Comparison ParseComparison(Project project, List<string> tokens, ref int position)
    {
        var field = FieldCatalog.Resolve(project, tokens[position++]);
        if (position >= tokens.Count)
        {
            throw new FormatException($"Missing operator after '{field.Key}'.");
        }

        var op = tokens[position++] switch
        {
            "=" or "==" => FilterOperator.Equals,
            "!=" or "<>" => FilterOperator.NotEquals,
            ">" => FilterOperator.GreaterThan,
            ">=" => FilterOperator.GreaterOrEqual,
            "<" => FilterOperator.LessThan,
            "<=" => FilterOperator.LessOrEqual,
            "~" => FilterOperator.Contains,
            var other => throw new FormatException($"Unknown operator '{other}'."),
        };
        if (position >= tokens.Count)
        {
            throw new FormatException($"Missing value after '{field.Key}'.");
        }

        var literalToken = tokens[position++];
        var literal = op == FilterOperator.Contains
            ? Unquote(literalToken)
            : FieldCatalog.ParseLiteral(field.Kind, Unquote(literalToken), project.TimeSettings);
        return new FilterNode.Comparison(field, op, literal);
    }

    private static bool Is(string token, string keyword) => string.Equals(token, keyword, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string token)
        => token.Length >= 2 && (token[0] == '"' || token[0] == '\'') && token[^1] == token[0]
            ? token[1..^1]
            : token;

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
            }
            else if (current is '(' or ')')
            {
                tokens.Add(current.ToString());
                index++;
            }
            else if (current is '"' or '\'')
            {
                var end = text.IndexOf(current, index + 1);
                if (end < 0)
                {
                    throw new FormatException("Unterminated string in filter.");
                }

                tokens.Add(text[index..(end + 1)]);
                index = end + 1;
            }
            else if (current is '=' or '!' or '<' or '>' or '~')
            {
                var length = index + 1 < text.Length && (text[index + 1] == '=' || (current == '<' && text[index + 1] == '>')) ? 2 : 1;
                tokens.Add(text.Substring(index, length));
                index += length;
            }
            else
            {
                var start = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ('(' or ')' or '=' or '!' or '<' or '>' or '~'))
                {
                    index++;
                }

                tokens.Add(text[start..index]);
            }
        }

        return tokens;
    }
}
