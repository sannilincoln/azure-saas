using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Saas.Identity.Claims;

/// <summary>
/// Microsoft Entra External ID compatibility shim.
///
/// The ASDK was built for Azure AD B2C, where the <c>sub</c> claim is the user's
/// directory object id (a GUID) and surfaces as <see cref="ClaimTypes.NameIdentifier"/>.
/// The app relies on that throughout (e.g. ApplicationUser.NameIdentifier, the
/// authorization handlers, GetNameIdentifierId()), parsing it as a <see cref="Guid"/>.
///
/// In Entra External ID, <c>sub</c> is an opaque, per-application pairwise identifier
/// (NOT a GUID); the directory object id lives in the <c>oid</c> claim. Left unmapped,
/// every NameIdentifier read throws (ArgumentNullException: NameIdentifier).
///
/// <see cref="TryMapObjectIdToNameIdentifier"/> replaces a missing/non-GUID
/// NameIdentifier with the <c>oid</c> value. It is used from the OIDC OnTokenValidated
/// event (web apps — runs on the sign-in principal and is baked into the auth cookie)
/// and as an <see cref="IClaimsTransformation"/> (APIs validating bearer tokens).
/// </summary>
public class NameIdentifierClaimsTransformation : IClaimsTransformation
{
    // Object id ('oid') under both the short JWT name and the mapped legacy URI.
    private static readonly string[] ObjectIdClaimTypes =
    {
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "oid",
    };

    /// <summary>
    /// Ensures <see cref="ClaimTypes.NameIdentifier"/> holds the directory object-id GUID.
    /// Returns true if a mapping was applied (or already valid), false if no usable
    /// object id was found. Idempotent.
    /// </summary>
    public static bool TryMapObjectIdToNameIdentifier(ClaimsIdentity identity)
    {
        var nameIdentifier = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(nameIdentifier, out _))
        {
            return true; // already a GUID (e.g. legacy B2C tokens)
        }

        var objectId = ObjectIdClaimTypes
            .Select(t => identity.FindFirst(t)?.Value)
            .FirstOrDefault(v => Guid.TryParse(v, out _));

        if (objectId is null)
        {
            return false;
        }

        var existing = identity.FindFirst(ClaimTypes.NameIdentifier);
        if (existing is not null)
        {
            identity.RemoveClaim(existing);
        }
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, objectId));
        return true;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is ClaimsIdentity { IsAuthenticated: true } identity)
        {
            TryMapObjectIdToNameIdentifier(identity);
        }

        return Task.FromResult(principal);
    }
}
