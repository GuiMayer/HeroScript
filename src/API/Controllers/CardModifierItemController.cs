using API.Contracts;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/card-modifier-items")]
public sealed class CardModifierItemController(ICardModifierItemQueryService items,
    ILogger<CardModifierItemController> logger) : BaseApiController(logger)
{
    [HttpGet]
    public IActionResult Read(Guid runId)
    {
        var result = items.Read(runId);
        return result.IsSuccess ? Ok(result.Value)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Modifier item query failed", result.Error);
    }
}
