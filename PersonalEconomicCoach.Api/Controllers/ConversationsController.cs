using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("conversations")]
public sealed class ConversationsController(IConversationService conversationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await conversationService.GetConversationsAsync(cancellationToken));

    [HttpGet("{id:long}/messages")]
    public async Task<ActionResult<IReadOnlyList<ConversationMessageResponse>>> GetMessages(
        long id,
        CancellationToken cancellationToken) =>
        Ok(await conversationService.GetMessagesAsync(id, cancellationToken));
}
