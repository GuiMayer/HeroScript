using API.Contracts;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/cards/presentations")]
public sealed class CardPresentationController(ICardPresentationQueryService cards,
    ILogger<CardPresentationController> logger) : BaseApiController(logger)
{
    [HttpGet]
    public IActionResult Read(Guid runId)
    {
        var result = cards.Read(runId);
        return result.IsSuccess ? Ok(result.Value)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Card presentation failed", result.Error);
    }
}
