using System.Text.RegularExpressions;

namespace ApiArchitect.Api.Features.Designs;

/// <summary>Starting points for a new design.</summary>
public static partial class DesignExamples
{
    /// <summary>An empty design whose title and namespace come from the design's name.</summary>
    public static DesignDocument Blank(string name)
    {
        var words = Words().Matches(name).Select(m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..]);
        var root = string.Concat(words);
        if (root.Length == 0 || char.IsDigit(root[0]) || root.Length > 60)
        {
            root = "My";
        }

        return new DesignDocument
        {
            Title = name.Trim(),
            Version = "1.0.0",
            Namespace = root.EndsWith("Api", StringComparison.Ordinal) ? root : $"{root}.Api",
        };
    }

    /// <summary>A small, complete CRUD API: enough to see every part of the designer and the generated code.</summary>
    public static DesignDocument Bookstore() => new()
    {
        Title = "Bookstore API",
        Version = "1.0.0",
        Description = "Manage the books in a small online bookstore.",
        Namespace = "Bookstore.Api",
        Models =
        [
            new()
            {
                Name = "Book",
                Description = "A book in the catalogue.",
                Properties =
                [
                    new() { Name = "Id", Type = "guid", Required = true },
                    new() { Name = "Title", Type = "string", Required = true, MaxLength = 200 },
                    new() { Name = "Author", Type = "string", Required = true, MaxLength = 100 },
                    new() { Name = "Isbn", Type = "string", Required = true, Pattern = @"97[89]\d{10}", Description = "ISBN-13, digits only." },
                    new() { Name = "Price", Type = "decimal", Required = true, Minimum = 0 },
                    new() { Name = "PublishedOn", Type = "date" },
                    new() { Name = "Tags", Type = "string", IsList = true },
                ],
            },
            new()
            {
                Name = "BookInput",
                Description = "What a client sends to add or change a book.",
                Properties =
                [
                    new() { Name = "Title", Type = "string", Required = true, MinLength = 1, MaxLength = 200 },
                    new() { Name = "Author", Type = "string", Required = true, MinLength = 1, MaxLength = 100 },
                    new() { Name = "Isbn", Type = "string", Required = true, Pattern = @"97[89]\d{10}", Description = "ISBN-13, digits only." },
                    new() { Name = "Price", Type = "decimal", Required = true, Minimum = 0, Maximum = 10000 },
                    new() { Name = "PublishedOn", Type = "date" },
                    new() { Name = "Tags", Type = "string", IsList = true },
                ],
            },
        ],
        Endpoints =
        [
            new()
            {
                Resource = "Books", Method = "GET", Route = "/api/books", Name = "ListBooks",
                Summary = "List books, optionally filtered by a search term.",
                Parameters =
                [
                    new() { Name = "search", In = "query", Type = "string", Description = "Matches the title or author." },
                    new() { Name = "page", In = "query", Type = "int", Description = "1-based page number." },
                ],
                ResponseModel = "Book", ResponseIsList = true, SuccessStatus = 200,
            },
            new()
            {
                Resource = "Books", Method = "GET", Route = "/api/books/{id}", Name = "GetBook", Summary = "Get a book.",
                Parameters = [new() { Name = "id", In = "path", Type = "guid" }],
                ResponseModel = "Book", SuccessStatus = 200, ErrorStatuses = [404],
            },
            new()
            {
                Resource = "Books", Method = "POST", Route = "/api/books", Name = "CreateBook", Summary = "Add a book.",
                RequestModel = "BookInput", ResponseModel = "Book", SuccessStatus = 201, ErrorStatuses = [400, 401],
                RequiresAuth = true,
            },
            new()
            {
                Resource = "Books", Method = "PUT", Route = "/api/books/{id}", Name = "UpdateBook", Summary = "Replace a book's details.",
                Parameters = [new() { Name = "id", In = "path", Type = "guid" }],
                RequestModel = "BookInput", ResponseModel = "Book", SuccessStatus = 200, ErrorStatuses = [400, 401, 404],
                RequiresAuth = true,
            },
            new()
            {
                Resource = "Books", Method = "DELETE", Route = "/api/books/{id}", Name = "DeleteBook", Summary = "Remove a book.",
                Parameters = [new() { Name = "id", In = "path", Type = "guid" }],
                SuccessStatus = 204, ErrorStatuses = [401, 404], RequiresAuth = true,
            },
        ],
    };

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Words();
}
