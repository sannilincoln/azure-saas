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
            // B2C emitted the email in the SOAP 'emailaddress' claim. Entra External ID
            // does not; it carries the sign-in email in 'preferred_username' (and sometimes
            // 'email'/'emails'). Try the B2C claim first, then fall back.
            var identity = Identity;
            var emailAddress = identity?.FindFirst(SR.EmailAddressClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(emailAddress) || !RegexUtilities.IsValidEmail(emailAddress))
            {
                emailAddress = new[] { "preferred_username", "email", "emails" }
                    .Select(claimType => identity?.FindFirst(claimType)?.Value)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && RegexUtilities.IsValidEmail(value))
                    ?? emailAddress;
            }

            if (!string.IsNullOrWhiteSpace(emailAddress) && RegexUtilities.IsValidEmail(emailAddress))
            {
                return emailAddress;
            }

            var claimTypes = identity is null
                ? "<no identity>"
                : string.Join(", ", identity.Claims.Select(c => c.Type));
            throw new ArgumentException(
                $"No valid email claim (emailaddress/preferred_username/email). Claim types present: [{claimTypes}]",
                nameof(EmailAddress));
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
