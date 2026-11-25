using System.Text.RegularExpressions;
using ApiArchitect.Api.Features.Environments;

namespace ApiArchitect.Api.Features.Collections;

public enum ResolvedAuthKind
{
    Bearer,
    ApiKeyHeader,
    ApiKeyQuery,
}

/// <param name="Kind">Where the credential goes.</param>
/// <param name="Value">The token or key value.</param>
/// <param name="Name">API key only: the header or query parameter name.</param>
public sealed record ResolvedAuth(ResolvedAuthKind Kind, string Value, string? Name);

/// <summary>A saved request with every variable filled in, ready to send.</summary>
/// <param name="SecretValues">
/// Decrypted secret values that ended up in the request, so history and logs can mask them wherever they appear.
/// </param>
public sealed record ResolvedRequest(
    string Method,
    Uri Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string? Body,
    string? ContentType,
    ResolvedAuth? Auth,
    IReadOnlyCollection<string> SecretValues);

/// <summary>Either a request ready to send, or the reasons it can't be built, written for the user.</summary>
public sealed record ResolveResult(ResolvedRequest? Request, IReadOnlyList<string> Errors);

/// <summary>
/// Turns a saved request plus an optional environment into the concrete HTTP request to send.
/// </summary>
/// <remarks>
/// Rules, in order:
/// <list type="number">
/// <item><c>{{name}}</c> is replaced in the URL, query parameters, headers, body, content type and auth values.
/// Values come from the environment's variables, plus <c>{{baseUrl}}</c> from its base URL. Substitution is a
/// single pass: a variable whose value contains <c>{{other}}</c> is not expanded again.</item>
/// <item>A URL that isn't absolute after substitution is appended to the environment's base URL.</item>
/// <item>Enabled query parameters are URL-encoded and appended to the URL.</item>
/// <item>Environment headers are sent first; a request header with the same name (any case) replaces them.</item>
/// <item>Auth "inherit" uses the environment's auth; "none" sends no auth.</item>
/// </list>
/// Any variable with no value is an error rather than being sent as literal <c>{{name}}</c> text.
/// </remarks>
public sealed partial class RequestResolver(EnvironmentSecrets secrets)
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.\-]*)\s*\}\}")]
    private static partial Regex VariablePattern();

    public ResolveResult Resolve(SavedRequest request, ApiEnvironment? environment)
    {
        var context = new Context(environment, secrets);

        var url = context.Substitute(request.Url).Trim();
        if (!IsHttpUrl(url))
        {
            if (environment?.BaseUrl is { } baseUrl)
            {
                url = url.Length == 0 ? baseUrl : $"{baseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
            }
        }

        var query = request.QueryParameters
            .Where(q => q.Enabled)
            .Select(q => $"{Uri.EscapeDataString(context.Substitute(q.Name))}={Uri.EscapeDataString(context.Substitute(q.Value))}")
            .ToList();

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in (environment?.Headers ?? []).Concat(request.Headers).Where(h => h.Enabled))
        {
            var name = context.Substitute(header.Name);
            headers.RemoveAll(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            headers.Add(new(name, context.Substitute(header.Value)));
        }

        var body = request.Body is null ? null : context.Substitute(request.Body);
        var contentType = request.ContentType is null ? null : context.Substitute(request.ContentType);
        var auth = ResolveAuth(request.Auth, environment, context);

        var errors = context.Errors();
        if (query.Count > 0)
        {
            url += (url.Contains('?') ? "&" : "?") + string.Join('&', query);
        }

        if (errors.Count == 0 && !IsHttpUrl(url))
        {
            errors.Add(environment is null
                ? "The URL must be absolute, such as https://api.example.com/orders, or select an environment with a base URL."
                : $"The URL must be absolute, or the environment \"{environment.Name}\" needs a base URL.");
        }

        return errors.Count > 0
            ? new ResolveResult(null, errors)
            : new ResolveResult(
                new ResolvedRequest(request.Method, new Uri(url), headers, body, contentType, auth, context.UsedSecrets),
                []);
    }

    private ResolvedAuth? ResolveAuth(AuthSettings auth, ApiEnvironment? environment, Context context)
    {
        if (auth.Type == AuthTypes.Inherit)
        {
            if (environment is null || environment.Auth.Type == AuthTypes.None || environment.Auth.ProtectedValue is null)
            {
                return null;
            }

            if (!secrets.TryUnprotect(environment.ProjectId, environment.Auth.ProtectedValue, out var stored))
            {
                context.AddError($"The auth value of environment \"{environment.Name}\" can't be decrypted. Enter it again.");
                return null;
            }

            context.UseSecret(stored);
            var value = context.Substitute(stored);
            context.UseSecret(value);
            return Build(environment.Auth.Type, value, environment.Auth.KeyName, environment.Auth.KeyLocation, context);
        }

        return auth.Type == AuthTypes.None
            ? null
            : Build(auth.Type, context.Substitute(auth.Value ?? ""), auth.KeyName, auth.KeyLocation, context);
    }

    private static ResolvedAuth Build(string type, string value, string? keyName, string? keyLocation, Context context) =>
        type == AuthTypes.Bearer
            ? new ResolvedAuth(ResolvedAuthKind.Bearer, value, null)
            : new ResolvedAuth(
                keyLocation == ApiKeyLocations.Query ? ResolvedAuthKind.ApiKeyQuery : ResolvedAuthKind.ApiKeyHeader,
                value,
                context.Substitute(keyName ?? ""));

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>The variables available to one resolution, and what went wrong while using them.</summary>
    private sealed class Context
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly HashSet<string> _secretNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _unreadableSecrets = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);
        private readonly List<string> _errors = [];
        private readonly ApiEnvironment? _environment;

        public HashSet<string> UsedSecrets { get; } = new(StringComparer.Ordinal);

        public Context(ApiEnvironment? environment, EnvironmentSecrets secrets)
        {
            _environment = environment;
            if (environment is null)
            {
                return;
            }

            foreach (var variable in environment.Variables)
            {
                if (!variable.IsSecret)
                {
                    _values[variable.Name] = variable.Value;
                }
                else if (secrets.TryUnprotect(environment.ProjectId, variable.Value, out var plaintext))
                {
                    _values[variable.Name] = plaintext;
                    _secretNames.Add(variable.Name);
                }
                else
                {
                    _unreadableSecrets.Add(variable.Name);
                }
            }

            if (environment.BaseUrl is not null)
            {
                _values.TryAdd(EnvironmentRules.BaseUrlVariable, environment.BaseUrl);
            }
        }

        public string Substitute(string template) =>
            VariablePattern().Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                if (_values.TryGetValue(name, out var value))
                {
                    if (_secretNames.Contains(name))
                    {
                        UseSecret(value);
                    }

                    return value;
                }

                _missing.Add(name);
                return match.Value;
            });

        public void UseSecret(string value)
        {
            if (value.Length > 0)
            {
                UsedSecrets.Add(value);
            }
        }

        public void AddError(string error) => _errors.Add(error);

        public List<string> Errors()
        {
            var errors = new List<string>(_errors);
            var unreadable = _missing.Where(_unreadableSecrets.Contains).ToList();
            foreach (var name in unreadable)
            {
                errors.Add($"The secret {name} can't be decrypted. Enter it again in the environment.");
            }

            var missing = _missing.Except(unreadable).Select(n => $"{{{{{n}}}}}").ToList();
            if (missing.Count > 0)
            {
                var list = string.Join(", ", missing);
                errors.Add(_environment is null
                    ? $"Select an environment: there's no value for {list}."
                    : $"The environment \"{_environment.Name}\" has no value for {list}.");
            }

            return errors;
        }
    }
}
