namespace FoodDelivery.Api.Common;

public class CommonResultDto<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public IDictionary<string, string[]>? Errors { get; set; }
    public T? Data { get; set; }

    public static CommonResultDto<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static CommonResultDto<T> Fail(string message, string? code = null, IDictionary<string, string[]>? errors = null) =>
        new() { Success = false, Message = message, Code = code, Errors = errors };
}
