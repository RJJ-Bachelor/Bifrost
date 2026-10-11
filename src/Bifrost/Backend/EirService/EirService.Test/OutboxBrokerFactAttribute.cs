namespace EirService.Test;

public sealed class OutboxBrokerFactAttribute : FactAttribute
{
    public OutboxBrokerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")))
            Skip = "Set BIFROST_TEST_POSTGRES and BIFROST_TEST_RABBITMQ to isolated test servers.";
    }
}
