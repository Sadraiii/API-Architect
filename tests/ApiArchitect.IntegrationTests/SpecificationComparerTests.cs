using ApiArchitect.Api.Features.Specifications;

namespace ApiArchitect.IntegrationTests;

public sealed class SpecificationComparerTests
{
    private const string Before = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Shop", "version": "1" },
          "paths": {
            "/orders": { "get": { "summary": "List" }, "post": { "summary": "Create" } },
            "/orders/{id}": { "delete": { "summary": "Delete" } }
          },
          "components": { "schemas": { "Order": { "type": "object" }, "Old": { "type": "string" } } }
        }
        """;

    [Fact]
    public void Lists_added_removed_and_changed_endpoints_and_schemas()
    {
        const string after = """
            {
              "openapi": "3.1.0",
              "info": { "title": "Shop", "version": "1" },
              "paths": {
                "/orders": { "get": { "summary": "List all" }, "post": { "summary": "Create" }, "put": { "summary": "Upsert" } }
              },
              "components": { "schemas": { "Order": { "type": "object" }, "New": { "type": "string" } } }
            }
            """;

        var changes = SpecificationComparer.Compare(Before, "1", after, "1");

        Assert.Equal(["PUT /orders"], changes.AddedEndpoints);
        Assert.Equal(["DELETE /orders/{id}"], changes.RemovedEndpoints);
        Assert.Equal(["GET /orders"], changes.ChangedEndpoints);
        Assert.Equal(["New"], changes.AddedSchemas);
        Assert.Equal(["Old"], changes.RemovedSchemas);
        Assert.Empty(changes.ChangedSchemas);
        Assert.False(changes.OtherChanges);
        Assert.True(changes.HasChanges);
    }

    [Fact]
    public void Formatting_and_key_order_are_not_changes()
    {
        var reordered = """{"components":{"schemas":{"Old":{"type":"string"},"Order":{"type":"object"}}},"info":{"version":"1","title":"Shop"},"openapi":"3.1.0","paths":{"/orders/{id}":{"delete":{"summary":"Delete"}},"/orders":{"post":{"summary":"Create"},"get":{"summary":"List"}}}}""";

        Assert.False(SpecificationComparer.Compare(Before, "1", reordered, "1").HasChanges);
    }

    [Fact]
    public void Other_changes_are_flagged()
    {
        var changes = SpecificationComparer.Compare(Before, "1", Before.Replace("\"Shop\"", "\"Shop API\""), "1");

        Assert.True(changes.OtherChanges);
        Assert.True(changes.HasChanges);
    }
}
