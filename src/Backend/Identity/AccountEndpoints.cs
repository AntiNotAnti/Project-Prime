using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MphRead.Backend.Data;
using MphRead.Backend.Profiles;
using MphRead.Identity;
using Npgsql;

namespace MphRead.Backend.Identity;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
// Keep the wire ID as text so malformed/empty JSON values reach the route's
// stable invalid_confirmation problem instead of framework model binding's
// untyped 400 response.
public sealed record ConfirmRequest(string? PlayerId, string? Code);
public sealed record ResendRequest(string Email);

public static class AccountEndpoints
{
    private static bool ValidEmail(string? value) => value is { Length: >= 3 and <= 254 }
        && value == value.Trim() && new EmailAddressAttribute().IsValid(value);
    private static bool ValidPassword(string? value) => value is { Length: >= 1 and <= 256 };

    public static void MapAccounts(this WebApplication app)
    {
        var confirmationTokens = new AccountConfirmationTokens(
            app.Services.GetRequiredService<IDataProtectionProvider>(),
            app.Services.GetRequiredService<IOptions<AccountOptions>>(),
            app.Services.GetRequiredService<TimeProvider>());
        var resendLimiter = new ConfirmationResendLimiter(
            app.Services.GetRequiredService<IOptions<AccountOptions>>(),
            app.Services.GetRequiredService<TimeProvider>());
        var auth = app.MapGroup("/v1/auth").RequireRateLimiting(BackendRoutePolicy.Auth);
        auth.MapPost("/register", Register);
        auth.MapPost("/login", async (LoginRequest request, SignInManager<HunterAccount> signIn) =>
        {
            if (!ValidEmail(request.Email) || !ValidPassword(request.Password))
                return BackendProblem.Create("invalid_credential", "Sign-in failed.", StatusCodes.Status401Unauthorized);
            signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
            var result = await signIn.PasswordSignInAsync(request.Email, request.Password,
                isPersistent: false, lockoutOnFailure: true);
            // No distinction between unknown, locked, unconfirmed or wrong-password accounts.
            // SignInManager writes the framework's bearer-token response on success.
            return result.Succeeded ? Results.Empty
                : BackendProblem.Create("invalid_credential", "Sign-in failed.", StatusCodes.Status401Unauthorized);
        });
        auth.MapPost("/refresh", async (RefreshRequest request, SignInManager<HunterAccount> signIn,
            IOptionsMonitor<BearerTokenOptions> tokens, TimeProvider clock) =>
        {
            if (request.RefreshToken is not { Length: >= 1 and <= 8192 })
                return BackendProblem.Create("invalid_refresh", "The refresh session is invalid.", StatusCodes.Status401Unauthorized);
            var ticket = tokens.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
            if (ticket?.Properties.ExpiresUtc is not { } expires || clock.GetUtcNow() >= expires
                || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not HunterAccount user
                || !await signIn.CanSignInAsync(user)
                || await signIn.UserManager.IsLockedOutAsync(user))
            {
                return BackendProblem.Create("invalid_refresh", "The refresh session is invalid.", StatusCodes.Status401Unauthorized);
            }
            var principal = await signIn.CreateUserPrincipalAsync(user);
            return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
        });
        auth.MapPost("/confirm-email", async (ConfirmRequest request, UserManager<HunterAccount> users) =>
        {
            if (!PlayerId.TryParse(request.PlayerId, out PlayerId playerId)
                || request.Code is not { Length: >= 1 and <= 4096 })
            {
                return BackendProblem.Create("invalid_confirmation", "The confirmation request is invalid.", StatusCodes.Status400BadRequest);
            }
            if (!confirmationTokens.TryUnprotect(request.Code, out string identityToken))
                return BackendProblem.Create("invalid_confirmation", "The confirmation request is invalid.", StatusCodes.Status400BadRequest);
            var user = await users.FindByIdAsync(playerId.ToString());
            if (user == null)
                return BackendProblem.Create("invalid_confirmation", "The confirmation request is invalid.", StatusCodes.Status400BadRequest);
            var result = await users.ConfirmEmailAsync(user, identityToken);
            return result.Succeeded ? Results.NoContent()
                : BackendProblem.Create("invalid_confirmation", "The confirmation request is invalid.", StatusCodes.Status400BadRequest);
        });
        auth.MapPost("/resend-confirmation", async (ResendRequest request, HttpContext http,
            UserManager<HunterAccount> users, IConfirmationEmail email, CancellationToken cancellationToken) =>
        {
            if (!ValidEmail(request.Email))
                return BackendProblem.Create("invalid_email", "The email address is invalid.", StatusCodes.Status400BadRequest);
            if (!email.IsConfigured)
                return BackendProblem.Create("confirmation_delivery_unavailable",
                    "Confirmation delivery is unavailable.", StatusCodes.Status503ServiceUnavailable);
            string normalized = users.NormalizeEmail(request.Email) ?? request.Email.ToUpperInvariant();
            if (!resendLimiter.TryAcquire(http.Connection.RemoteIpAddress, normalized)) return Results.Accepted();
            var user = await users.FindByEmailAsync(request.Email);
            if (user is { EmailConfirmed: false })
            {
                string code = confirmationTokens.Protect(await users.GenerateEmailConfirmationTokenAsync(user));
                await email.TrySendAsync(user.Email!, new PlayerId(user.Id), code, cancellationToken);
            }
            return Results.Accepted();
        });
        auth.MapPost("/revoke-sessions", async (HttpContext http, UserManager<HunterAccount> users) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
                return BackendProblem.Create("invalid_credential", "The session is invalid.",
                    StatusCodes.Status401Unauthorized);
            var user = await users.GetUserAsync(http.User);
            if (user == null)
                return BackendProblem.Create("invalid_credential", "The session is invalid.", StatusCodes.Status401Unauthorized);
            var result = await users.UpdateSecurityStampAsync(user);
            // Existing access tokens expire normally; refresh tokens fail stamp validation.
            return result.Succeeded ? Results.NoContent()
                : BackendProblem.Create("service_busy", "The account service is temporarily unavailable.",
                    StatusCodes.Status409Conflict);
        });
    }

    private static async Task<IResult> Register(RegisterRequest request, UserManager<HunterAccount> users,
        BackendDbContext db, IOptions<AccountOptions> settings, IConfirmationEmail email,
        IDataProtectionProvider protection, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!ValidEmail(request.Email) || !ValidPassword(request.Password)
            || !ProfileEndpoints.ValidDisplayName(request.DisplayName))
        {
            return BackendProblem.Create(!ValidEmail(request.Email) ? "invalid_email"
                : !ValidPassword(request.Password) ? "invalid_password" : "invalid_display_name",
                "The registration request is invalid.", StatusCodes.Status400BadRequest);
        }
        if (settings.Value.RequireConfirmedEmail && !email.IsConfigured)
        {
            return BackendProblem.Create("confirmation_delivery_unavailable",
                "Confirmation delivery is unavailable.", StatusCodes.Status503ServiceUnavailable);
        }
        var user = new HunterAccount { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    return BackendProblem.Create("duplicate_account", "The account already exists.",
                        StatusCodes.Status409Conflict);
                }
                return BackendProblem.Create("invalid_password",
                    "The password does not meet account requirements.", StatusCodes.Status400BadRequest);
            }
            db.Profiles.Add(new PlayerProfile { PlayerId = user.Id, DisplayName = request.DisplayName, FavoriteHunter = Hunter.Samus });
            db.Licenses.Add(new HunterLicense { PlayerId = user.Id, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return BackendProblem.Create("duplicate_account", "The account already exists.",
                StatusCodes.Status409Conflict);
        }
        bool confirmationDelivered = true;
        if (email.IsConfigured)
        {
            // Account commit precedes external delivery. A delivery failure leaves an
            // unconfirmed account; resend is the explicit recovery path.
            var confirmationTokens = new AccountConfirmationTokens(protection, settings, clock);
            string code = confirmationTokens.Protect(await users.GenerateEmailConfirmationTokenAsync(user));
            confirmationDelivered = await email.TrySendAsync(user.Email!, new PlayerId(user.Id), code, cancellationToken);
        }
        return Results.Created($"/v1/players/{user.Id:D}/license", new
        {
            PlayerId = new PlayerId(user.Id), ConfirmationRequired = settings.Value.RequireConfirmedEmail,
            ConfirmationDeliveryPending = settings.Value.RequireConfirmedEmail && !confirmationDelivered
        });
    }
}
