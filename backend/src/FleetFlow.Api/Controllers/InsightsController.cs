using System.ComponentModel.DataAnnotations;
using FleetFlow.Api.Contracts;
using FleetFlow.Api.Security;
using FleetFlow.Application.Insights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

public sealed class AiAnalysisRequest
{
    [Required, StringLength(64, MinimumLength = 64), RegularExpression("^[a-f0-9]{64}$")]
    public required string SnapshotFingerprint { get; init; }
}

[Route("api/v1/insights")]
[Authorize(Policy = SessionSecurity.InsightRead)]
public sealed class InsightsController(IInsightQueries queries, AiAnalysisService analysis) : ApiControllerBase
{
    /// <summary>Server-paginated verified operational findings, optionally scoped to a shipment.</summary>
    [HttpGet]
    [ProducesResponseType<PageResponse<OperationalFinding>>(200)]
    public async Task<PageResponse<OperationalFinding>> List([FromQuery] PagingQuery paging, [FromQuery] Guid? shipmentId, CancellationToken token)
    {
        var page = await queries.ListAsync(paging.ToPage(), shipmentId, token);
        return new(page.Items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);
    }
    /// <summary>Real database-backed counts and the fingerprint of a bounded six-finding AI sample.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<InsightSummary>(200)]
    public async Task<InsightSummary> Summary(CancellationToken token) => (await queries.SnapshotAsync(token)).Summary;

    /// <summary>Configuration, explicit account authorization and persisted UTC quota availability; never generates AI.</summary>
    [HttpGet("ai-availability")]
    [ProducesResponseType<AiAvailability>(200)]
    public Task<AiAvailability> Availability(CancellationToken token) => analysis.AvailabilityAsync(Guid.Parse(Actor), token);

    /// <summary>Explicit on-demand analysis. Requires allowlisted account, CSRF and a current summary fingerprint.</summary>
    [HttpPost("ai-analysis")]
    [RequestSizeLimit(4096)]
    [Authorize(Policy = SessionSecurity.AiGenerate)]
    [ProducesResponseType<AiAnalysis>(200)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    public Task<AiAnalysis> Generate(AiAnalysisRequest request, CancellationToken token) => analysis.GenerateAsync(Guid.Parse(Actor), request.SnapshotFingerprint, token);
}
