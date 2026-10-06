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
        public static void EnsureSeedData(WebApplication app)
        {
            using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.Migrate();

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
            }
        }

        private static void EnsureUser(
            UserManager<ApplicationUser> userMgr,
            string userName,
            string email,
            string displayName)
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
                    claim.Type == JwtClaimTypes.Role && claim.Value == "Teacher"))
            {
                var result = userMgr.AddClaimAsync(
                    user,
                    new Claim(JwtClaimTypes.Role, "Teacher")).Result;

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not set the Teacher role for seed user '{userName}': " +
                        string.Join(", ", result.Errors.Select(error => error.Description)));
                }
            }
        }
    }
}
