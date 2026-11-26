using System.ComponentModel.DataAnnotations;

namespace ApiArchitect.Api.Features.ClientCode;

/// <summary>What to generate from a specification.</summary>
/// <param name="Template"><c>console</c> for a Console App that calls the API once, or <c>worker</c> for a Worker Service that calls it on a schedule.</param>
/// <param name="ProjectName">The Visual Studio project name. It is also the namespace of the generated code.</param>
/// <param name="Operations">The operations to include, each as <c>METHOD /path</c>, for example <c>GET /pets/{id}</c>.</param>
public sealed record ClientCodeRequest(
    [Required, AllowedValues(ClientTemplates.Console, ClientTemplates.Worker)] string Template,
    [Required, StringLength(100), RegularExpression(ClientCodeRules.ProjectNamePattern,
        ErrorMessage = "Use letters, digits and underscores, with dots between parts. It can't start with a digit.")]
    string ProjectName,
    [Required, MinLength(1), MaxLength(ClientCodeRules.MaxOperations)] string[] Operations);

/// <param name="Steps">What to do in Visual Studio, in order. Code and menu names are in backticks.</param>
/// <param name="Files">The code to paste, in the order the steps mention it.</param>
/// <param name="Notes">Parts of the specification the generated code leaves out or simplifies.</param>
public sealed record ClientCodeResponse(
    string Template,
    string ProjectName,
    IReadOnlyList<string> Steps,
    IReadOnlyList<ClientCodeFile> Files,
    IReadOnlyList<string> Notes);

/// <param name="FileName">The file name in the Visual Studio project.</param>
/// <param name="Where">One line on where the code goes: a new file, or replacing a file the template made.</param>
public sealed record ClientCodeFile(string FileName, string Where, string Content);

public static class ClientTemplates
{
    public const string Console = "console";
    public const string Worker = "worker";
}

public static class ClientCodeRules
{
    public const string ProjectNamePattern = @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$";
    public const int MaxOperations = 500;
}
