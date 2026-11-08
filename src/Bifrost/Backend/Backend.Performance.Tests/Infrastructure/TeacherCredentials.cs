namespace Backend.Performance.Tests.Infrastructure;

/// <summary>Runtime-only credentials. Passwords never enter configuration files or reports.</summary>
public sealed class TeacherCredentials(int number, string username, string password)
{
    public int Number { get; } = number;
    public string Username { get; } = username;
    public string Password { get; } = password;

    public static TeacherCredentials[] FromEnvironment()
    {
        var credentials = Enumerable.Range(1, 30).Select(number =>
        {
            var usernameVariable = $"BIFROST_PERF_TEACHER_{number}_USERNAME";
            var passwordVariable = $"BIFROST_PERF_TEACHER_{number}_PASSWORD";
            var username = Environment.GetEnvironmentVariable(usernameVariable);
            var password = Environment.GetEnvironmentVariable(passwordVariable);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                throw new ArgumentException($"Set {usernameVariable} and {passwordVariable} for all thirty teachers.");
            return new TeacherCredentials(number, username, password);
        }).ToArray();
        if (credentials.Select(value => value.Username).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 30)
            throw new ArgumentException("Thirty distinct teacher usernames are required.");
        return credentials;
    }

    public override string ToString() => $"Teacher {Number} (credentials redacted)";
}
