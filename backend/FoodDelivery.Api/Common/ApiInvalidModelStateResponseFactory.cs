using Microsoft.AspNetCore.Mvc;

namespace FoodDelivery.Api.Common;

public static class ApiInvalidModelStateResponseFactory
{
    public static IActionResult Create(ActionContext context)
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        return new BadRequestObjectResult(
            CommonResultDto<object?>.Fail("Dữ liệu không hợp lệ", "ERR_VALIDATION", errors));
    }
}
