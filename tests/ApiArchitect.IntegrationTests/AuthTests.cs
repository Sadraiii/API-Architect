using System.Net;
using System.Net.Http.Json;
using ApiArchitect.Api.Features.Auth;
using Microsoft.AspNetCore.Mvc;

namespace ApiArchitect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_signs_the_user_in()
    {
        var (client, email) = await factory.CreateSignedInClientAsync();

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", Ct);

        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Me_without_signing_in_returns_401()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_correct_password_sets_cookie()
    {
        var (_, email) = await factory.CreateSignedInClientAsync();
        var client = factory.CreateAnonymousClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = TestPasswords.Valid }, Ct);
        var me = await client.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_problem()
    {
        var (_, email) = await factory.CreateSignedInClientAsync();

        var response = await factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/login", new { email, password = "not the password at all" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Register_with_short_password_returns_validation_problem()
    {
        var response = await factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/register", new { email = "short@example.com", password = "short" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.Contains("PasswordTooShort", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Register_with_existing_email_returns_validation_problem()
    {
        var (_, email) = await factory.CreateSignedInClientAsync();

        var response = await factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/register", new { email, password = TestPasswords.Valid }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var (client, _) = await factory.CreateSignedInClientAsync();

        var logout = await client.PostAsync("/api/auth/logout", content: null, Ct);
        var me = await client.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Intro_is_unseen_until_marked_and_stays_seen_after_login()
    {
        var (client, email) = await factory.CreateSignedInClientAsync();

        var before = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", Ct);
        var mark = await client.PostAsync("/api/auth/me/intro-seen", content: null, Ct);
        var after = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", Ct);

        var login = await factory.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/login", new { email, password = TestPasswords.Valid }, Ct);
        var loggedIn = await login.Content.ReadFromJsonAsync<CurrentUserResponse>(Ct);

        Assert.False(before!.IntroSeen);
        Assert.Equal(HttpStatusCode.NoContent, mark.StatusCode);
        Assert.True(after!.IntroSeen);
        Assert.True(loggedIn!.IntroSeen);
    }

    [Fact]
    public async Task Intro_seen_is_per_user()
    {
        var (first, _) = await factory.CreateSignedInClientAsync();
        var (second, _) = await factory.CreateSignedInClientAsync();

        await first.PostAsync("/api/auth/me/intro-seen", content: null, Ct);
        var me = await second.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", Ct);

        Assert.False(me!.IntroSeen);
    }

    [Fact]
    public async Task Marking_intro_seen_without_signing_in_returns_401()
    {
        var response = await factory.CreateAnonymousClient().PostAsync("/api/auth/me/intro-seen", content: null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
