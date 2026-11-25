using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ApiArchitect.Api.Features.Environments;

/// <summary>
/// Encrypts environment secrets with ASP.NET Core Data Protection (AES-256-CBC + HMACSHA256 by default).
/// Each project gets its own sub-purpose, so ciphertext copied from one project's row into another's
/// fails to decrypt instead of leaking.
/// </summary>
/// <remarks>
/// Secrets are only as durable as the key ring. If the keys are lost (a redeploy without persisted keys),
/// existing secrets can't be decrypted and have to be entered again; <see cref="TryUnprotect"/> reports that
/// instead of throwing.
/// </remarks>
public sealed class EnvironmentSecrets(IDataProtectionProvider provider)
{
    private const string Purpose = "ApiArchitect.Environments.Secrets.v1";

    public string Protect(Guid projectId, string plaintext) => ForProject(projectId).Protect(plaintext);

    public bool TryUnprotect(Guid projectId, string ciphertext, out string plaintext)
    {
        try
        {
            plaintext = ForProject(projectId).Unprotect(ciphertext);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = "";
            return false;
        }
    }

    private IDataProtector ForProject(Guid projectId) => provider.CreateProtector(Purpose, projectId.ToString());
}
