using Saas.Application.Web.Interfaces;
using Saas.Application.Web.Utilities;
using System.Security.Claims;

namespace Saas.Application.Web.Models;

public class ApplicationUser : ClaimsIdentity, IApplicationUser
{
    private static ClaimsIdentity? Identity => AppHttpContext.Current?.User?.Identity as ClaimsIdentity;

    public string EmailAddress
    {
        get
        {
            var claim = Identity?.FindFirst(SR.EmailAddressClaimType);
            string emailAddress = claim?.Value ?? string.Empty;

            return (!string.IsNullOrWhiteSpace(emailAddress) || RegexUtilities.IsValidEmail(emailAddress)) ? emailAddress : (emailAddress == null) ? throw new ArgumentNullException("EmailAddress") : throw new ArgumentException("The email addres must be in a valid format", "EmailAddress");
        }
    }

    public Guid NameIdentifier
    {
        get
        {
            var identity = Identity;

            // B2C put the object-id GUID in 'sub'/NameIdentifier. Entra External ID's
            // NameIdentifier ('sub') is an opaque pairwise id; the object-id GUID is in 'oid'.
            var value = identity?.FindFirst(SR.NameIdentifierClaimType)?.Value;
            if (!Guid.TryParse(value, out Guid nameIdentifier))
            {
                value = identity?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                        ?? identity?.FindFirst("oid")?.Value;
            }

            if (Guid.TryParse(value, out nameIdentifier))
            {
                return nameIdentifier;
            }

            var claimTypes = identity is null
                ? "<no identity>"
                : string.Join(", ", identity.Claims.Select(c => c.Type));
            throw new ArgumentNullException(nameof(NameIdentifier),
                $"No GUID name identifier (NameIdentifier/oid). Claim types present: [{claimTypes}]");
        }
    }

    public string AuthenticationClassReference
    {
        get
        {
            var claim = Identity?.FindFirst(SR.AuthenticationClassReferenceClaimType);

            return claim?.Value ?? string.Empty;
        }
    }

    public DateTime AuthenticationTime
    {
        get
        {
            var claim = Identity?.FindFirst(SR.AuthenticationTimeClaimType);

            bool success = long.TryParse(claim?.Value, out long ticks);

            return new DateTime((success) ? ticks : 0);
        }
    }

    public long AuthenticationTimeTicks
    {
        get
        {
            var claim = Identity?.FindFirst(SR.AuthenticationTimeClaimType);

            bool success = long.TryParse(claim?.Value, out long ticks);

            return (success) ? ticks : 0;
        }
    }

    public string GivenName
    {
        get
        {
            var claim = Identity?.FindFirst(SR.GivenNameClaimType);

            return claim?.Value ?? string.Empty;
        }
    }

    public string Surname
    {
        get
        {
            var claim = Identity?.FindFirst(SR.SurnameClaimType);

            return claim?.Value ?? string.Empty;
        }
    }

    public Guid TenantId
    {
        get
        {
            var claim = Identity?.FindFirst(SR.TenantIdClaimType);

            return (Guid.TryParse(claim?.Value, out Guid tenantId)) ? tenantId : throw new ArgumentNullException("TenantId");
        }
    }
}
