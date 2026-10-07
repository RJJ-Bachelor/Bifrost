using Duende.IdentityModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Claims;
using VarService.Data;
using VarService.Models;

namespace VarService
{
    public class SeedData
    {
        public static void EnsureDatabase(WebApplication app)
        {
            using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // Older development databases were created with EnsureCreated and have
                // the initial Identity schema, but no EF migration history. Adopt that
                // schema without recreating tables or deleting existing users.
                context.Database.ExecuteSqlRaw("""
                    DO $$
                    BEGIN
                        IF to_regclass('"AspNetUsers"') IS NOT NULL
                            AND to_regclass('"AspNetRoles"') IS NOT NULL
                            AND to_regclass('"AspNetUserClaims"') IS NOT NULL
                            AND to_regclass('"AspNetRoleClaims"') IS NOT NULL
                            AND to_regclass('"AspNetUserLogins"') IS NOT NULL
                            AND to_regclass('"AspNetUserRoles"') IS NOT NULL
                            AND to_regclass('"AspNetUserTokens"') IS NOT NULL THEN
                            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                                "MigrationId" character varying(150) PRIMARY KEY,
                                "ProductVersion" character varying(32) NOT NULL);
                            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                                VALUES ('20261006180757_init', '10.0.1')
                                ON CONFLICT ("MigrationId") DO NOTHING;
                        END IF;
                    END $$;
                    """);
                context.Database.Migrate();
            }
        }

        public static void EnsureSeedData(WebApplication app)
        {
            using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                EnsureUser(
                    userMgr,
                    userName: "knud",
                    email: "KnudHansen@example.com",
                    displayName: "Knud Hansen");

                EnsureUser(
                    userMgr,
                    userName: "bob",
                    email: "BobJensen@example.com",
                    displayName: "Bob Jensen");

                if (app.Environment.IsDevelopment())
                {
                    EnsureUser(userMgr, "learner", "learner@example.com", "Learner", role: "Student");
                }
            }
        }

        private static void EnsureUser(
            UserManager<ApplicationUser> userMgr,
            string userName,
            string email,
            string displayName,
            string role = "Teacher")
        {
            var user = userMgr.FindByNameAsync(userName).Result;
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = userName,
                    Email = email,
                    EmailConfirmed = true
                };

                var result = userMgr.CreateAsync(user, "Pass123$").Result;
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not create seed user '{userName}': " +
                        string.Join(", ", result.Errors.Select(error => error.Description)));
                }

                Log.Information("Seed user {UserName} created", userName);
            }
            else
            {
                Log.Debug("Seed user {UserName} already exists", userName);
            }

            var nameClaim = userMgr.GetClaimsAsync(user).Result
                .FirstOrDefault(claim => claim.Type == JwtClaimTypes.Name);

            if (nameClaim?.Value != displayName)
            {
                var result = nameClaim == null
                    ? userMgr.AddClaimAsync(user, new Claim(JwtClaimTypes.Name, displayName)).Result
                    : userMgr.ReplaceClaimAsync(
                        user,
                        nameClaim,
                        new Claim(JwtClaimTypes.Name, displayName)).Result;

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not set the name claim for seed user '{userName}': " +
                        string.Join(", ", result.Errors.Select(error => error.Description)));
                }
            }

            if (!userMgr.GetClaimsAsync(user).Result.Any(claim =>
                    claim.Type == JwtClaimTypes.Role && claim.Value == role))
            {
                var result = userMgr.AddClaimAsync(
                    user,
                    new Claim(JwtClaimTypes.Role, role)).Result;

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not set the {role} role for seed user '{userName}': " +
                        string.Join(", ", result.Errors.Select(error => error.Description)));
                }
            }
        }
    }
}
