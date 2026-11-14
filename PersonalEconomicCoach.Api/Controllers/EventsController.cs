using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("events")]
public sealed class EventsController(IEventService eventService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlannedEventResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await eventService.GetAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(PlannedEventResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEventRequest request, CancellationToken cancellationToken)
    {
        var created = await eventService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ApiException(400, "id must be greater than zero.");
        var deleted = await eventService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound(new ErrorResponse("Event not found."));
    }
}
