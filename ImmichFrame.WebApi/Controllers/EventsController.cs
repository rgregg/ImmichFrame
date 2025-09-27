using System.ComponentModel.DataAnnotations;
using ImmichFrame.Core.Events;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models.Events;
using ImmichFrame.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ImmichFrame.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IFrameEventQueue _queue;
    private readonly FrameEventValidator _validator;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IFrameEventQueue queue, FrameEventValidator validator, ILogger<EventsController> logger)
    {
        _queue = queue;
        _validator = validator;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> PostEvent([FromBody] FrameEventRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var frameEvent = _validator.Validate(request);
            var enqueued = await _queue.EnqueueAsync(frameEvent, cancellationToken);

            if (!enqueued)
            {
                return Conflict(new { message = "Event already exists" });
            }

            return Accepted();
        }
        catch (ValidationException vex)
        {
            _logger.LogWarning(vex, "Invalid frame event received with id {EventId}", request?.Id);
            return BadRequest(new { message = vex.Message });
        }
    }

    [HttpGet("next")]
    public async Task<IActionResult> GetNext([FromQuery] string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new { message = "deviceId is required" });
        }

        var frameEvent = await _queue.PeekNextAsync(deviceId, cancellationToken);

        if (frameEvent is null)
        {
            return NoContent();
        }

        return Ok(FrameEventResponseDto.FromDomain(frameEvent));
    }

    [HttpPost("{eventId}/ack")]
    public async Task<IActionResult> AckEvent(string eventId, [FromQuery] string deviceId, [FromBody] FrameEventAckRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new { message = "deviceId is required" });
        }

        var removed = await _queue.AckAsync(deviceId, eventId, request.Status, cancellationToken);

        if (!removed)
        {
            return NotFound();
        }

        _logger.LogDebug("Acked event {EventId} for {DeviceId} with status {Status}", eventId, deviceId, request.Status);
        return NoContent();
    }
}
