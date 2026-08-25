namespace FoodDelivery.Api.Options;

public sealed class TenantResolutionOptions
{
    public const string SectionName = "TenantResolution";
    public string BaseDomain { get; init; } = string.Empty;
    public string[] SharedHosts { get; init; } = Array.Empty<string>();
}
