namespace FoodDelivery.Api.Common;

public class ApiErrorPayload
{
    public string Message { get; set; } = default!;
    public string? Code { get; set; }
    public IDictionary<string, string[]>? Errors { get; set; }
}
