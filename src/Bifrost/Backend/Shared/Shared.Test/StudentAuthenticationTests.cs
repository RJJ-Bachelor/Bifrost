using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Api.Authentication;

namespace Shared.Test;

public sealed class StudentAuthenticationTests
{
    private const string StudentId = "11111111111111111111111111111111";

    [Fact]
    public void ProtectedStudentCookieRoundtripsItsIdentityAndExpiration()
    {
        using var services = Services();
        var cookie = services.GetRequiredService<StudentIdentityCookie>();
        var before = DateTimeOffset.UtcNow;

        var protectedValue = cookie.Protect(StudentId);

        Assert.True(cookie.TryUnprotect(protectedValue, out var userId, out var expires));
        Assert.Equal(StudentId, userId);
        Assert.InRange(expires, before.Add(StudentIdentityCookie.Lifetime), DateTimeOffset.UtcNow.Add(StudentIdentityCookie.Lifetime));
        Assert.True(cookie.TryUnprotect(protectedValue, out var simpleUserId));
        Assert.Equal(StudentId, simpleUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-base64!")]
    [InlineData("YWJjZA")]
    public void InvalidStudentCookiesNeverProduceAnIdentity(string? value)
    {
        using var services = Services();

        Assert.False(services.GetRequiredService<StudentIdentityCookie>().TryUnprotect(value, out var userId, out var expires));

        Assert.Null(userId);
        Assert.Equal(default, expires);
    }

    [Theory]
    [InlineData("not-a-guid", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", false)]
    [InlineData(StudentId, true)]
    public void ExpiredCookiesAndProtectedValuesWithInvalidIdentityAreRejected(string identity, bool expire)
    {
        using var services = Services();
        var provider = services.GetRequiredService<IOptionsMonitor<StudentCookieOptions>>().Get(StudentCookieHandler.SchemeName).DataProtectionProvider!;
        var protector = provider.CreateProtector(StudentIdentityCookie.ProtectionPurpose).ToTimeLimitedDataProtector();
        var protectedValue = protector.Protect(identity, DateTimeOffset.UtcNow.AddDays(expire ? -1 : 1));

        Assert.False(services.GetRequiredService<StudentIdentityCookie>().TryUnprotect(protectedValue, out var userId));

        Assert.Null(userId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndTamperedCookiesCannotAuthenticateAStudent(bool tampered)
    {
        using var services = Services();
        var context = Context(services);
        if (tampered) context.Request.Headers.Cookie = StudentCookieHandler.CookieName + "=tampered";

        var result = await context.AuthenticateAsync(StudentCookieHandler.SchemeName);

        Assert.False(result.Succeeded);
        Assert.Equal(!tampered, result.None);
        Assert.Equal(tampered, result.Failure is not null);
    }

    [Fact]
    public async Task ValidCookieAuthenticatesOnlyTheSignedStudentIdentityAndRole()
    {
        using var services = Services();
        var context = Context(services);
        context.Request.Headers.Cookie = StudentCookieHandler.CookieName + "=" + services.GetRequiredService<StudentIdentityCookie>().Protect(StudentId);

        var result = await context.AuthenticateAsync(StudentCookieHandler.SchemeName);

        Assert.True(result.Succeeded);
        Assert.Equal(StudentId, result.Principal!.FindFirst("sub")!.Value);
        Assert.True(result.Principal.IsInRole("Student"));
        Assert.False(result.Principal.IsInRole("Teacher"));
        Assert.Equal(StudentCookieHandler.SchemeName, result.Ticket!.AuthenticationScheme);
    }

    [Theory]
    [InlineData("/api/teachers/notifications", "token", null, "token")]
    [InlineData("/api/teachers/notifications/negotiate", "token", null, "token")]
    [InlineData("/api/students/notifications", "token", null, null)]
    [InlineData("/api/teachers/notifications-other", "token", null, null)]
    [InlineData("/api/teachers/notifications", "", null, null)]
    [InlineData("/api/teachers/notifications", "query", "bEaReR header", null)]
    public async Task QueryTokensAreLimitedToTheTeacherHubAndNeverOverrideBearerHeaders(string path, string token, string? authorization, string? expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.QueryString = QueryString.Create("access_token", token);
        if (authorization is not null) http.Request.Headers.Authorization = authorization;
        var context = new MessageReceivedContext(http,
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions());

        await NotificationHubAuthentication.ReadQueryToken(context);

        Assert.Equal(expected, context.Token);
    }

    [Theory]
    [InlineData("Teacher", "teacher", "Bifrost", true)]
    [InlineData("Teacher", "teacher", "openid Bifrost profile", true)]
    [InlineData("Teacher", "teacher", "OtherBifrost", false)]
    [InlineData("Teacher", "teacher", null, false)]
    [InlineData("Teacher", null, "Bifrost", false)]
    [InlineData("Student", "student", "Bifrost", false)]
    public async Task TeacherAuthorizationRequiresRoleSubjectAndAnExactScope(string role, string? subject, string? scope, bool expected)
    {
        using var services = Services();
        var claims = new List<Claim> { new("role", role) };
        if (subject is not null) claims.Add(new("sub", subject));
        if (scope is not null) claims.Add(new("scope", scope));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "sub", "role"));

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, "Teacher");

        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData("/api/students/help-requests", StudentCookieHandler.SchemeName)]
    [InlineData("/api/teachers/help-requests", JwtBearerDefaults.AuthenticationScheme)]
    [InlineData("/api/students-other", JwtBearerDefaults.AuthenticationScheme)]
    public void EndpointAuthenticationSelectsTheAppropriateCredentialType(string path, string expected)
    {
        using var services = Services();
        var options = services.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>().Get("EndpointAuthentication");
        var http = new DefaultHttpContext();
        http.Request.Path = path;

        Assert.Equal(expected, options.ForwardDefaultSelector!(http));
    }

    private static DefaultHttpContext Context(IServiceProvider services) => new() { RequestServices = services };

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<StudentCookieOptions>(StudentCookieHandler.SchemeName,
            options => options.DataProtectionProvider = new EphemeralDataProtectionProvider());
        services.AddBifrostServiceAuthentication(new ConfigurationBuilder().Build(), new TestEnvironment());
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Shared.Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
