using System.Security.Claims;
using ApiArchitect.Api.Data;
using ApiArchitect.Api.Features.Collections;
using ApiArchitect.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ApiArchitect.Api.Features.Environments;

/// <summary>
/// Environments: base URL, variables, shared headers and default auth per target (Local, Staging, ...).
/// Which environment is active is a choice in the UI, passed when a request runs, so switching
/// environments needs no server state.
/// </summary>
public static class EnvironmentEndpoints
{
    public static void MapEnvironmentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Environments").RequireAuthorization();

        api.MapGet("/projects/{projectId:guid}/environments", List)
            .WithSummary("List a project's environments by name. Secret values are never included.");
        api.MapPost("/projects/{projectId:guid}/environments", Create).WithSummary("Create an environment.");
        api.MapGet("/environments/{id:guid}", Get).WithName("GetEnvironment").WithSummary("Get an environment.");
        api.MapPut("/environments/{id:guid}", Update)
            .WithSummary("Replace an environment. Send a secret's value as null to keep the stored one.");
        api.MapDelete("/environments/{id:guid}", Delete).WithSummary("Delete an environment.");
    }

    private static async Task<Results<Ok<List<EnvironmentResponse>>, NotFound>> List(
        Guid projectId, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var environments = await db.Environments
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(environments.Select(ToResponse).ToList());
    }

    private static async Task<Results<Ok<EnvironmentResponse>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var environment = await db.Environments
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id && e.Project.OwnerId == userId, cancellationToken);

        return environment is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(environment));
    }

    private static async Task<Results<CreatedAtRoute<EnvironmentResponse>, NotFound, ValidationProblem>> Create(
        Guid projectId, EnvironmentInput input, ClaimsPrincipal user, AppDbContext db, EnvironmentSecrets secrets,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNowForStorage();
        var environment = new ApiEnvironment { ProjectId = projectId, Name = "", CreatedAt = now, UpdatedAt = now };
        var errors = Apply(input, environment, secrets);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        db.Environments.Add(environment);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.CreatedAtRoute(ToResponse(environment), "GetEnvironment", new { id = environment.Id });
    }

    private static async Task<Results<Ok<EnvironmentResponse>, NotFound, ValidationProblem>> Update(
        Guid id, EnvironmentInput input, ClaimsPrincipal user, AppDbContext db, EnvironmentSecrets secrets,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var environment = await db.Environments
            .SingleOrDefaultAsync(e => e.Id == id && e.Project.OwnerId == userId, cancellationToken);
        if (environment is null)
        {
            return TypedResults.NotFound();
        }

        var errors = Apply(input, environment, secrets);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        environment.UpdatedAt = clock.GetUtcNowForStorage();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(environment));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var deleted = await db.Environments
            .Where(e => e.Id == id && e.Project.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// Validates the input and, only if it's valid, copies it onto the environment. Secrets sent as null keep
    /// the value already stored under the same name, so a rename means entering the secret again.
    /// </summary>
    private static Dictionary<string, string[]> Apply(EnvironmentInput input, ApiEnvironment environment, EnvironmentSecrets secrets)
    {
        var errors = new Dictionary<string, string[]>();

        var baseUrl = string.IsNullOrWhiteSpace(input.BaseUrl) ? null : input.BaseUrl.Trim();
        if (baseUrl is not null
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")))
        {
            errors["BaseUrl"] = ["The base URL must be an absolute http or https URL, such as https://localhost:5001."];
        }

        var storedSecrets = environment.Variables.Where(v => v.IsSecret).ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        var variables = new List<EnvironmentVariable>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (variable, index) in (input.Variables ?? []).Select((v, i) => (v, i)))
        {
            if (!names.Add(variable.Name))
            {
                errors[$"Variables[{index}].Name"] = [$"There's more than one variable named {variable.Name}."];
                continue;
            }

            if (!variable.IsSecret)
            {
                variables.Add(new EnvironmentVariable { Name = variable.Name, Value = variable.Value ?? "" });
            }
            else if (variable.Value is not null)
            {
                variables.Add(new EnvironmentVariable
                {
                    Name = variable.Name,
                    Value = secrets.Protect(environment.ProjectId, variable.Value),
                    IsSecret = true,
                });
            }
            else if (storedSecrets.TryGetValue(variable.Name, out var ciphertext))
            {
                variables.Add(new EnvironmentVariable { Name = variable.Name, Value = ciphertext, IsSecret = true });
            }
            else
            {
                errors[$"Variables[{index}].Value"] = [$"The secret {variable.Name} needs a value."];
            }
        }

        if (input.Headers?.Any(h => string.IsNullOrWhiteSpace(h.Name)) == true)
        {
            errors["Headers"] = ["Every header needs a name."];
        }

        var auth = BuildAuth(input.Auth, environment, secrets, errors);

        if (errors.Count == 0)
        {
            environment.Name = input.Name.Trim();
            environment.BaseUrl = baseUrl;
            environment.Variables = variables;
            environment.Headers = SavedRequestMapping.ToEntries(input.Headers);
            environment.Auth = auth;
        }

        return errors;
    }

    private static EnvironmentAuth BuildAuth(
        EnvironmentAuthInput? input, ApiEnvironment environment, EnvironmentSecrets secrets, Dictionary<string, string[]> errors)
    {
        if (input is null || input.Type == AuthTypes.None)
        {
            return new EnvironmentAuth { Type = AuthTypes.None };
        }

        if (input.Type == AuthTypes.ApiKey && (string.IsNullOrWhiteSpace(input.KeyName) || input.KeyLocation is null))
        {
            errors["Auth.KeyName"] = ["An API key needs a name and a location (header or query)."];
        }

        var stored = environment.Auth.Type != AuthTypes.None ? environment.Auth.ProtectedValue : null;
        var protectedValue = input.Value is not null ? secrets.Protect(environment.ProjectId, input.Value) : stored;
        if (protectedValue is null)
        {
            errors["Auth.Value"] = [input.Type == AuthTypes.Bearer ? "Enter the bearer token." : "Enter the API key value."];
        }

        return new EnvironmentAuth
        {
            Type = input.Type,
            ProtectedValue = protectedValue,
            KeyName = input.Type == AuthTypes.ApiKey ? input.KeyName?.Trim() : null,
            KeyLocation = input.Type == AuthTypes.ApiKey ? input.KeyLocation : null,
        };
    }

    private static EnvironmentResponse ToResponse(ApiEnvironment e) => new(
        e.Id,
        e.ProjectId,
        e.Name,
        e.BaseUrl,
        e.Variables.Select(v => new VariableResponse(v.Name, v.IsSecret ? null : v.Value, v.IsSecret)).ToList(),
        SavedRequestMapping.ToDtos(e.Headers),
        new EnvironmentAuthResponse(e.Auth.Type, e.Auth.KeyName, e.Auth.KeyLocation, e.Auth.ProtectedValue is not null),
        e.CreatedAt,
        e.UpdatedAt);
}
