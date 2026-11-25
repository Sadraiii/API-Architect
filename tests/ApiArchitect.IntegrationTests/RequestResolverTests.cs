using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Features.Environments;
using Microsoft.AspNetCore.DataProtection;

namespace ApiArchitect.IntegrationTests;

/// <summary>Variable substitution and request assembly. Pure logic, no database.</summary>
public sealed class RequestResolverTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();
    private readonly EnvironmentSecrets _secrets = new(new EphemeralDataProtectionProvider());
    private RequestResolver Resolver => new(_secrets);

    private ApiEnvironment Staging(params (string Name, string Value, bool Secret)[] variables) => new()
    {
        ProjectId = ProjectId,
        Name = "Staging",
        BaseUrl = "https://staging.example.com/api/",
        Variables = variables
            .Select(v => new EnvironmentVariable
            {
                Name = v.Name,
                Value = v.Secret ? _secrets.Protect(ProjectId, v.Value) : v.Value,
                IsSecret = v.Secret,
            })
            .ToList(),
    };

    private static SavedRequest Request(string url) => new() { Name = "Test", Method = "GET", Url = url };

    [Fact]
    public void Relative_url_is_appended_to_the_base_url_and_query_is_encoded()
    {
        var request = Request("/orders/{{orderId}}");
        request.QueryParameters =
        [
            new KeyValueEntry { Name = "q", Value = "a b&c" },
            new KeyValueEntry { Name = "skipped", Value = "x", Enabled = false },
        ];

        var result = Resolver.Resolve(request, Staging(("orderId", "42", false)));

        Assert.Empty(result.Errors);
        Assert.Equal("https://staging.example.com/api/orders/42?q=a%20b%26c", result.Request!.Url.AbsoluteUri);
    }

    [Fact]
    public void BaseUrl_variable_is_available_and_absolute_urls_are_left_alone()
    {
        var fromVariable = Resolver.Resolve(Request("{{baseUrl}}health"), Staging());
        var absolute = Resolver.Resolve(Request("https://other.example.com/x"), Staging());

        Assert.Equal("https://staging.example.com/api/health", fromVariable.Request!.Url.AbsoluteUri);
        Assert.Equal("https://other.example.com/x", absolute.Request!.Url.AbsoluteUri);
    }

    [Fact]
    public void Missing_variables_are_listed_instead_of_sent_literally()
    {
        var result = Resolver.Resolve(Request("https://x.example.com/{{tenant}}/{{missing}}"), Staging());

        Assert.Null(result.Request);
        Assert.Contains("{{missing}}", Assert.Single(result.Errors));
        Assert.Contains("{{tenant}}", result.Errors[0]);
    }

    [Fact]
    public void Without_an_environment_a_relative_url_is_an_error()
    {
        var result = Resolver.Resolve(Request("/orders"), environment: null);

        Assert.Contains("must be absolute", Assert.Single(result.Errors));
    }

    [Fact]
    public void Substitution_is_a_single_pass()
    {
        var request = Request("https://x.example.com/");
        request.Body = "{{outer}}";

        var result = Resolver.Resolve(request, Staging(("outer", "{{inner}}", false), ("inner", "nope", false)));

        Assert.Equal("{{inner}}", result.Request!.Body);
    }

    [Fact]
    public void Request_headers_replace_environment_headers_of_the_same_name()
    {
        var environment = Staging();
        environment.Headers =
        [
            new KeyValueEntry { Name = "Accept", Value = "application/xml" },
            new KeyValueEntry { Name = "X-Env", Value = "staging" },
        ];
        var request = Request("/x");
        request.Headers = [new KeyValueEntry { Name = "accept", Value = "application/json" }];

        var headers = Resolver.Resolve(request, environment).Request!.Headers;

        Assert.Equal(
            [new("X-Env", "staging"), new("accept", "application/json")],
            headers);
    }

    [Fact]
    public void Inherited_auth_uses_the_decrypted_environment_value_and_reports_it_as_secret()
    {
        var environment = Staging();
        environment.Auth = new EnvironmentAuth
        {
            Type = AuthTypes.ApiKey,
            KeyName = "X-Api-Key",
            KeyLocation = ApiKeyLocations.Header,
            ProtectedValue = _secrets.Protect(ProjectId, "key-123"),
        };

        var resolved = Resolver.Resolve(Request("/x"), environment).Request!;

        Assert.Equal(new ResolvedAuth(ResolvedAuthKind.ApiKeyHeader, "key-123", "X-Api-Key"), resolved.Auth);
        Assert.Contains("key-123", resolved.SecretValues);
    }

    [Fact]
    public void Auth_none_overrides_the_environment()
    {
        var environment = Staging();
        environment.Auth = new EnvironmentAuth { Type = AuthTypes.Bearer, ProtectedValue = _secrets.Protect(ProjectId, "t") };
        var request = Request("/x");
        request.Auth = new AuthSettings { Type = AuthTypes.None };

        Assert.Null(Resolver.Resolve(request, environment).Request!.Auth);
    }

    [Fact]
    public void Secret_variables_used_in_the_request_are_reported_for_masking()
    {
        var request = Request("/x");
        request.Auth = new AuthSettings { Type = AuthTypes.Bearer, Value = "{{token}}" };

        var resolved = Resolver.Resolve(request, Staging(("token", "s3cret", true), ("unused", "other", true))).Request!;

        Assert.Equal("s3cret", resolved.Auth!.Value);
        Assert.Equal(["s3cret"], resolved.SecretValues);
    }

    [Fact]
    public void A_secret_from_another_project_cannot_be_decrypted()
    {
        var environment = Staging();
        environment.Variables.Add(new EnvironmentVariable
        {
            Name = "token",
            Value = _secrets.Protect(Guid.CreateVersion7(), "stolen"),
            IsSecret = true,
        });

        var result = Resolver.Resolve(Request("/{{token}}"), environment);

        Assert.Contains("can't be decrypted", Assert.Single(result.Errors));
    }
}
