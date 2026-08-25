using System.Text.Json;
using FoodDelivery.Api.Common;
using FoodDelivery.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FoodDelivery.Api.Tests.Common;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Domain_exception_becomes_conflict_response()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DomainException("SHOP_INACTIVE", "Shop inactive"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("SHOP_INACTIVE", json.RootElement.GetProperty("code").GetString());
    }
}
