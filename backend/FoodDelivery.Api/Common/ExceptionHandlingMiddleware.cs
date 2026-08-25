using System.Net;
using System.Text.Json;
using FoodDelivery.Domain.Common;

namespace FoodDelivery.Api.Common;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await WriteErrorAsync(context, CommonResultDto<object?>.Fail(ex.Message, ex.Code));
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteErrorAsync(context, CommonResultDto<object?>.Fail(ex.Message, ex.Code));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, CommonResultDto<object?>.Fail("Đã có lỗi không xác định xảy ra", "ERR_INTERNAL"));
        }
    }

    private static Task WriteErrorAsync(HttpContext context, CommonResultDto<object?> payload)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }
}
