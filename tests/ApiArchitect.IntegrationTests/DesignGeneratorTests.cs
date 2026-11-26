using ApiArchitect.Api.Features.Designs;
using ApiArchitect.Api.Features.Specifications;

namespace ApiArchitect.IntegrationTests;

/// <summary>The validator and generators on their own, without the web app or a database.</summary>
public sealed class DesignGeneratorTests
{
    [Fact]
    public void The_example_design_is_valid()
    {
        Assert.Empty(DesignValidator.Validate(DesignExamples.Bookstore()));
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var first = CSharpProjectGenerator.Zip(CSharpProjectGenerator.Generate(DesignExamples.Bookstore()));
        var second = CSharpProjectGenerator.Zip(CSharpProjectGenerator.Generate(DesignExamples.Bookstore()));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Validation_rules_become_DataAnnotations_and_optional_properties_become_nullable()
    {
        var model = Generate(DesignExamples.Bookstore(), "Bookstore.Api/Models/BookInput.cs");

        Assert.Contains("[Required, StringLength(200, MinimumLength = 1)] string Title,", model);
        Assert.Contains("[Required, RegularExpression(@\"97[89]\\d{10}\")] string Isbn,", model);
        Assert.Contains("[Range(0d, 10000d)] decimal Price,", model);
        Assert.Contains("DateOnly? PublishedOn,", model);
        Assert.Contains("List<string>? Tags);", model);
    }

    [Fact]
    public void Handlers_declare_every_status_in_their_signature()
    {
        var endpoints = Generate(DesignExamples.Bookstore(), "Bookstore.Api/Endpoints/BooksEndpoints.cs");

        Assert.Contains("var group = app.MapGroup(\"/api/books\").WithTags(\"Books\");", endpoints);
        Assert.Contains("private static Results<Ok<Book>, ValidationProblem, UnauthorizedHttpResult, NotFound> UpdateBook(Guid id, BookInput request, CancellationToken cancellationToken)", endpoints);
        Assert.Contains("private static Ok<List<Book>> ListBooks(string? search, int? page, CancellationToken cancellationToken)", endpoints);
    }

    [Fact]
    public void Auth_adds_JWT_bearer_only_when_an_endpoint_needs_it()
    {
        var withAuth = CSharpProjectGenerator.Generate(DesignExamples.Bookstore());
        var withoutAuth = CSharpProjectGenerator.Generate(DesignExamples.Bookstore() with
        {
            Endpoints = [.. DesignExamples.Bookstore().Endpoints.Select(e => e with { RequiresAuth = false })],
        });

        Assert.Contains("AddJwtBearer()", withAuth.Single(f => f.Path.EndsWith("Program.cs")).Content);
        Assert.DoesNotContain("AddJwtBearer()", withoutAuth.Single(f => f.Path.EndsWith("Program.cs")).Content);
        Assert.DoesNotContain(withoutAuth, f => f.Path.Contains("BearerSecurityTransformer"));
    }

    [Fact]
    public void Optional_properties_are_nullable_and_patterns_are_anchored_in_OpenAPI()
    {
        var document = DesignOpenApiWriter.Build(DesignExamples.Bookstore());
        var properties = document["components"]!["schemas"]!["BookInput"]!["properties"]!;

        Assert.Equal("""["string","null"]""", properties["publishedOn"]!["type"]!.ToJsonString());
        Assert.Equal("""^(?:97[89]\d{10})$""", properties["isbn"]!["pattern"]!.GetValue<string>());
        Assert.Equal("""[{"Bearer":[]}]""", document["paths"]!["/api/books"]!["post"]!["security"]!.ToJsonString());
    }

    [Fact]
    public void A_model_referencing_itself_generates_and_parses()
    {
        var design = new DesignDocument
        {
            Title = "Categories",
            Version = "1.0.0",
            Namespace = "Shop",
            Models =
            [
                new()
                {
                    Name = "Category",
                    Properties =
                    [
                        new() { Name = "Name", Type = "string", Required = true },
                        new() { Name = "Parent", Type = "Category" },
                        new() { Name = "Children", Type = "Category", IsList = true },
                    ],
                },
            ],
            Endpoints =
            [
                new() { Resource = "Categories", Method = "POST", Route = "/categories", Name = "CreateCategory", RequestModel = "Category", SuccessStatus = 204 },
            ],
        };

        Assert.Empty(DesignValidator.Validate(design));
        Assert.Empty(OpenApiImport.Read(DesignOpenApiWriter.Write(design)).Errors);
        Assert.Contains("\"children\": [", Generate(design, "Shop/Shop.http"));
    }

    [Theory]
    [InlineData("/api/books/{id}", "needs a path parameter called id")]
    [InlineData("/api//books", "isn't a valid route segment")]
    [InlineData("api/books", "must start with /")]
    public void Bad_routes_are_reported(string route, string expected)
    {
        var design = DesignExamples.Bookstore() with
        {
            Endpoints = [new() { Resource = "Books", Method = "GET", Route = route, Name = "Get" }],
        };

        var messages = DesignValidator.Validate(design).Values.SelectMany(m => m);

        Assert.Contains(messages, m => m.Contains(expected));
    }

    [Fact]
    public void Names_that_break_the_generated_code_are_rejected()
    {
        var design = DesignExamples.Bookstore() with
        {
            Models = [.. DesignExamples.Bookstore().Models, new() { Name = "Task" }, new() { Name = "Book" }],
        };

        var messages = DesignValidator.Validate(design).Values.SelectMany(m => m).ToList();

        Assert.Contains(messages, m => m.Contains("Task clashes"));
        Assert.Contains(messages, m => m.Contains("more than one model called Book"));
    }

    private static string Generate(DesignDocument design, string path) =>
        CSharpProjectGenerator.Generate(design).Single(f => f.Path == path).Content;
}
