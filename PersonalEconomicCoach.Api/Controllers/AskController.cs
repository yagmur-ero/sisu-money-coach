using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Middleware;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("ask")]
public sealed class AskController(
    IQuestionRouter questionRouter,
    IInsightService insightService,
    IAnswerGenerator answerGenerator,
    IConversationService conversationService,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AskResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AskResponse>> Ask(
        [FromBody] AskRequest request,
        CancellationToken cancellationToken)
    {
        var type = questionRouter.Resolve(request.QuestionId, request.Text);
        object data = type switch
        {
            "baskets" => await insightService.GetBasketsAsync(cancellationToken),
            "comparison" => await insightService.GetComparisonAsync(cancellationToken),
            "leaks" => await insightService.GetLeaksAsync(cancellationToken),
            "safe_to_spend" => await insightService.GetSafeToSpendAsync(cancellationToken),
            "what_if" => await insightService.GetWhatIfAsync(
                request.WhatIf ?? GetDefaultWhatIf(), cancellationToken),
            _ => throw new ApiException(422, "Unsupported insight type.")
        };

        var answer = await answerGenerator.GenerateAsync(
            request.Text, request.Tone, type, data, cancellationToken);

        var conversationId = await conversationService.SaveExchangeAsync(
            request.ConversationId,
            request.Text,
            answer,
            type,
            data,
            cancellationToken);

        return Ok(new AskResponse(type, answer, data, conversationId));
    }

    private WhatIfRequest GetDefaultWhatIf() => new()
    {
        RestaurantsPct = configuration.GetValue<decimal>("WhatIfDefaults:RestaurantsPct", 20m),
        ShoppingEur = configuration.GetValue<decimal>("WhatIfDefaults:ShoppingEur", 30m),
        ExtraSavingsEur = configuration.GetValue<decimal>("WhatIfDefaults:ExtraSavingsEur", 100m)
    };
}
