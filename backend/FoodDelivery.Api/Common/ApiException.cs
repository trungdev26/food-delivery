namespace FoodDelivery.Api.Common;

public class ApiException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }

    public ApiException(int statusCode, string message, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
