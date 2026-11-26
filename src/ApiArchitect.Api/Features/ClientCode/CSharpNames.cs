using System.Text;

namespace ApiArchitect.Api.Features.ClientCode;

/// <summary>Turns names from an OpenAPI document into valid C# identifiers.</summary>
public static class CSharpNames
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];

    public static bool IsKeyword(string name) => Keywords.Contains(name);

    /// <summary><c>pet_type</c>, <c>pet-type</c> and <c>petType</c> all become <c>PetType</c>.</summary>
    public static string Pascal(string? text)
    {
        var result = new StringBuilder();
        var upperNext = true;
        foreach (var c in text ?? "")
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                result.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        if (result.Length == 0)
        {
            return "Value";
        }

        return char.IsAsciiDigit(result[0]) ? "_" + result : result.ToString();
    }

    /// <summary>A parameter or variable name: <c>petId</c>, or <c>@class</c> for a keyword.</summary>
    public static string Camel(string? text)
    {
        var pascal = Pascal(text);
        var camel = pascal[0] == '_' ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return IsKeyword(camel) ? "@" + camel : camel;
    }

    /// <summary>Text for a one-line XML doc comment.</summary>
    public static string XmlText(string? text)
    {
        var oneLine = string.Join(' ', (text ?? "").Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (oneLine.Length > 200)
        {
            oneLine = oneLine[..197] + "...";
        }

        return oneLine.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    /// <summary>A C# string literal.</summary>
    public static string Literal(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    /// <summary>Returns <paramref name="name"/>, or the name with a number added if it's already taken.</summary>
    public static string Unique(string name, ISet<string> taken)
    {
        var candidate = name;
        for (var i = 2; !taken.Add(candidate); i++)
        {
            candidate = name + i;
        }

        return candidate;
    }
}
