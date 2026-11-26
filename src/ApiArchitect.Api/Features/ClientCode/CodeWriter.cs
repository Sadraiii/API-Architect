using System.Text;

namespace ApiArchitect.Api.Features.ClientCode;

/// <summary>Builds a source file line by line, always with \n line endings.</summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _text = new();

    public int Length => _text.Length;

    public void Line(string line = "") => _text.Append(line).Append('\n');

    public void Raw(string text) => _text.Append(text);

    /// <summary>A one-line <c>/// &lt;summary&gt;</c>, or nothing when there's no text.</summary>
    public void Summary(string? text, string indent = "")
    {
        var xml = CSharpNames.XmlText(text);
        if (xml.Length > 0)
        {
            Line($"{indent}/// <summary>{xml}</summary>");
        }
    }

    public override string ToString() => _text.ToString();
}
