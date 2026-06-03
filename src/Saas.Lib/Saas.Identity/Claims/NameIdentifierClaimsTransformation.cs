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
/// This transformation makes External ID behave like B2C did: if NameIdentifier is
/// missing or not a GUID, it is replaced with the value of the <c>oid</c> claim.
/// Idempotent and safe to run on every request.
/// </summary>
public class NameIdentifierClaimsTransformation : IClaimsTransformation
{
    private const string ObjectIdUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ObjectIdShort = "oid";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var nameIdentifier = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Already a GUID (e.g. legacy B2C tokens) — nothing to do.
        if (Guid.TryParse(nameIdentifier, out _))
        {
            return Task.FromResult(principal);
        }

        var objectId = identity.FindFirst(ObjectIdUri)?.Value
                       ?? identity.FindFirst(ObjectIdShort)?.Value;

        if (Guid.TryParse(objectId, out _))
        {
            // Replace the non-GUID NameIdentifier (the pairwise 'sub') with the object id.
            var existing = identity.FindFirst(ClaimTypes.NameIdentifier);
            if (existing is not null)
            {
                identity.RemoveClaim(existing);
            }
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, objectId));
        }

        return Task.FromResult(principal);
    }
}
