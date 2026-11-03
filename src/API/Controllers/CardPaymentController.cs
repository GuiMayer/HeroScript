using API.Contracts;
using Core.Run;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/cards/payment-preview")]
public sealed class CardPaymentController(ICardPaymentQueryService payments, ILogger<CardPaymentController> logger) : BaseApiController(logger)
{
    [HttpPost]
    [ProducesResponseType(typeof(CardPaymentPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult Preview(Guid combatId, [FromBody] CardPaymentPreviewRequest request)
    {
        var result = payments.Preview(combatId, request);
        if (result.IsSuccess) return Ok(result.Value);
        return result.Error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal)
            ? ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Payment version conflict", result.Error)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Payment preview failed", result.Error);
    }
}
