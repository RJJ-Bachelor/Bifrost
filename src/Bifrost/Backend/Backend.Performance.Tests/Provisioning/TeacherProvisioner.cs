using System.Security.Claims;
using Backend.Performance.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarService.Data;
using VarService.Models;

namespace Backend.Performance.Tests.Provisioning;

/// <summary>
/// Explicit administrative setup for an existing isolated test identity database. Uses Var's
/// real ASP.NET Identity stores/password policy; every subsequent login still goes through OIDC.
/// </summary>
public static class TeacherProvisioner
{
    private const string MarkerType = "bifrost:performance-owner";
    private const string MarkerValue = "Backend.Performance.Tests/v1";

    public static Task ProvisionAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(delete: false, cancellationToken);

    public static Task DeleteAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(delete: true, cancellationToken);

    private static async Task ExecuteAsync(bool delete, CancellationToken cancellationToken)
    {
        var credentials = TeacherCredentials.FromEnvironment();
        var connectionString = Environment.GetEnvironmentVariable("BIFROST_PERF_IDENTITY_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Set BIFROST_PERF_IDENTITY_CONNECTION_STRING to the existing isolated Var test database.");

        var services = new ServiceCollection();
        services.AddLogging(); // No providers: EF/Identity cannot emit connection strings or credentials.
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken))
            throw new InvalidOperationException("The existing Var identity database is unavailable; start the test environment first.");

        // Never call EnsureCreated/Migrate: schema ownership remains with the existing Var service.
        _ = await db.Users.AsNoTracking().Take(1).ToArrayAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var credential in credentials)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = await users.FindByNameAsync(credential.Username);
            if (user is not null)
            {
                var claims = await users.GetClaimsAsync(user);
                if (!claims.Any(claim => claim.Type == MarkerType && claim.Value == MarkerValue))
                    throw new InvalidOperationException($"Teacher slot {credential.Number} names an existing account not owned by this performance project. Choose a new performance username.");
                if (delete)
                {
                    RequireSuccess(await users.DeleteAsync(user), credential.Number, "delete");
                    continue;
                }
                if (!await users.CheckPasswordAsync(user, credential.Password) ||
                    !claims.Any(claim => claim.Type == "role" && claim.Value == "Teacher"))
                    throw new InvalidOperationException($"Provisioned teacher slot {credential.Number} does not match the supplied password/Teacher role. Existing accounts are never reset.");
                continue;
            }
            if (delete) continue;

            user = new ApplicationUser
            {
                UserName = credential.Username,
                Email = $"bifrost-performance-{Guid.NewGuid():N}@example.invalid",
                EmailConfirmed = true
            };
            RequireSuccess(await users.CreateAsync(user, credential.Password), credential.Number, "create");
            RequireSuccess(await users.AddClaimsAsync(user,
            [
                new Claim("name", $"Performance Teacher {credential.Number}"),
                new Claim("role", "Teacher"),
                new Claim(MarkerType, MarkerValue)
            ]), credential.Number, "assign claims");
        }
        await transaction.CommitAsync(cancellationToken);

        // A successful transaction must also be observable through the actual identity store.
        foreach (var credential in credentials)
        {
            var user = await users.FindByNameAsync(credential.Username);
            if (delete && user is not null || !delete && user is null)
                throw new InvalidOperationException($"Teacher slot {credential.Number} could not be verified after administrative setup.");
        }
    }

    private static void RequireSuccess(IdentityResult result, int slot, string operation)
    {
        if (!result.Succeeded)
            // Codes convey policy failures without echoing account input or credentials.
            throw new InvalidOperationException($"Cannot {operation} teacher slot {slot}: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
    }
}
