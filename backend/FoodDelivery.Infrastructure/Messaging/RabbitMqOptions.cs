namespace FoodDelivery.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public int NetworkRecoverySeconds { get; set; } = 5;
    public int RequestedHeartbeatSeconds { get; set; } = 30;
    public bool TlsEnabled { get; set; }
    public string TlsServerName { get; set; } = string.Empty;

    public void EnsureValid()
    {
        var errors = new List<string>();
        Required(HostName, "RabbitMq:HostName", errors);
        Required(UserName, "RabbitMq:UserName", errors);
        Required(Password, "RabbitMq:Password", errors);
        Required(VirtualHost, "RabbitMq:VirtualHost", errors);
        Required(ConnectionName, "RabbitMq:ConnectionName", errors);
        Required(ExchangeName, "RabbitMq:ExchangeName", errors);

        if (Port is < 1 or > 65535) errors.Add("RabbitMq:Port must be between 1 and 65535.");
        if (NetworkRecoverySeconds < 1) errors.Add("RabbitMq:NetworkRecoverySeconds must be positive.");
        if (RequestedHeartbeatSeconds < 1) errors.Add("RabbitMq:RequestedHeartbeatSeconds must be positive.");
        if (TlsEnabled) Required(TlsServerName, "RabbitMq:TlsServerName", errors);

        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    private static void Required(string value, string path, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{path} is required.");
    }
}
