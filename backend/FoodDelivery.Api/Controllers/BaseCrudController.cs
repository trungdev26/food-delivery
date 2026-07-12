using FoodDelivery.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FoodDelivery.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseCrudController<TDto, TCreateDto, TUpdateDto, TKey> : ControllerBase
{
    protected readonly IBaseService<TDto, TCreateDto, TUpdateDto, TKey> Service;

    protected BaseCrudController(IBaseService<TDto, TCreateDto, TUpdateDto, TKey> service)
    {
        Service = service;
    }

    [HttpGet]
    public virtual async Task<ActionResult<IEnumerable<TDto>>> GetAll()
    {
        return Ok(await Service.GetAllAsync());
    }

    [HttpGet("{id}")]
    public virtual async Task<ActionResult<TDto>> GetById(TKey id)
    {
        var result = await Service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public virtual async Task<ActionResult<TDto>> Create(TCreateDto dto)
    {
        var created = await Service.CreateAsync(dto);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id}")]
    public virtual async Task<ActionResult<TDto>> Update(TKey id, TUpdateDto dto)
    {
        var updated = await Service.UpdateAsync(id, dto);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(TKey id)
    {
        var deleted = await Service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
