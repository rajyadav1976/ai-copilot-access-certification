using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI;
using AI.Copilot.Access.Certification.Components.Certification.AI.Agent;
using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Session;

namespace AI.Copilot.Access.Certification.Components.Certification;

/// <summary>
/// API controller for AI recommendation operations.
/// Provides endpoints for triggering, retrieving, and giving feedback on AI recommendations.
/// </summary>
[Route("api/v1/certifications/ai-recommendations")]
[ApiController]
[Produces("application/json")]
public class AIRecommendationController : ControllerBase
{
    private readonly IAIRecommendationService _aiRecommendationService;
    private readonly IRecommendationEngine _recommendationEngine;
    private readonly IReviewerAssistant _reviewerAssistant;
    private readonly IFeedbackAnalyticsService _feedbackAnalyticsService;
    private readonly ISessionContext _sessionContext;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IAIGenerationTracker _generationTracker;
    private readonly ILogger<AIRecommendationController> _logger;

    public AIRecommendationController(
        IAIRecommendationService aiRecommendationService,
        IRecommendationEngine recommendationEngine,
        IReviewerAssistant reviewerAssistant,
        IFeedbackAnalyticsService feedbackAnalyticsService,
        ISessionContext sessionContext,
        IServiceScopeFactory serviceScopeFactory,
        IAIGenerationTracker generationTracker,
        ILogger<AIRecommendationController> logger)
    {
        _aiRecommendationService = aiRecommendationService;
        _recommendationEngine = recommendationEngine;
        _reviewerAssistant = reviewerAssistant;
        _feedbackAnalyticsService = feedbackAnalyticsService;
        _sessionContext = sessionContext;
        _serviceScopeFactory = serviceScopeFactory;
        _generationTracker = generationTracker;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all AI recommendations for a certification campaign.
    /// </summary>
    /// <param name="certificationProcessId">The campaign ID.</param>
    [HttpGet("{certificationProcessId:long}")]
    public async Task<IActionResult> GetRecommendations(long certificationProcessId)
    {
        var recommendations = await _aiRecommendationService.GetRecommendationsAsync(certificationProcessId);
        return Ok(recommendations);
    }

    /// <summary>
    /// Retrieves the AI recommendation for a specific review item.
    /// </summary>
    /// <param name="certificationProcessId">The campaign ID.</param>
    /// <param name="stepId">The review item step ID.</param>
    [HttpGet("{certificationProcessId:long}/step/{stepId:long}")]
    public async Task<IActionResult> GetRecommendationByStep(long certificationProcessId, long stepId)
    {
        var recommendation = await _aiRecommendationService.GetRecommendationByStepIdAsync(
            certificationProcessId, stepId);

        if (recommendation == null)
        {
            return NotFound(new { message = $"No AI recommendation found for step {stepId}" });
        }

        return Ok(recommendation);
    }

    /// <summary>
    /// Retrieves a summary of AI recommendations for a campaign.
    /// Checks the in-memory generation tracker to report "Processing" status
    /// even when the database has no records yet (during background generation).
    /// </summary>
    /// <param name="certificationProcessId">The campaign ID.</param>
    [HttpGet("{certificationProcessId:long}/summary")]
    public async Task<IActionResult> GetSummary(long certificationProcessId)
    {
        var summary = await _aiRecommendationService.GetRecommendationSummaryAsync(certificationProcessId);

        // If the tracker says we're processing but DB has no results yet, override status
        if (_generationTracker.IsProcessing(certificationProcessId)
            && summary.Status is "NotGenerated" or "NoItems" or "Failed")
        {
            summary.Status = "Processing";
        }

        return Ok(summary);
    }

    /// <summary>
    /// Triggers AI recommendation generation for a certification campaign.
    /// Generation runs asynchronously in the background. The endpoint returns immediately
    /// with a "Processing" status. Clients should poll the summary endpoint to track progress.
    /// </summary>
    /// <param name="certificationProcessId">The campaign ID.</param>
    /// <param name="request">Optional parameters for generation.</param>
    [HttpPost("{certificationProcessId:long}/generate")]
    public async Task<IActionResult> GenerateRecommendations(
        long certificationProcessId,
        [FromBody] GenerateRecommendationsRequest? request = null)
    {
        var triggeredBy = _sessionContext.PrincipalContext?.DisplayName ?? "system";
        var forceRegenerate = request?.ForceRegenerate ?? false;

        _logger.LogInformation(
            "AI recommendation generation requested for campaign {CertificationProcessId} by {TriggeredBy}",
            certificationProcessId, triggeredBy);

        try
        {
            // Quick check: if already generated and not forcing, return existing summary
            if (!forceRegenerate)
            {
                var existingSummary = await _aiRecommendationService.GetRecommendationSummaryAsync(certificationProcessId);
                if (existingSummary.Status == "Generated")
                {
                    return Ok(existingSummary);
                }
            }

            // Capture session context info for background scope initialization
            var tenantContext = _sessionContext.TenantContext;
            var principalContext = _sessionContext.PrincipalContext;
            var securityContext = _sessionContext.SecurityContext;

            // Return "Processing" immediately
            var processingSummary = new AIRecommendationSummary
            {
                CertificationProcessId = certificationProcessId,
                Status = "Processing"
            };

            // Mark as processing in the tracker (survives across requests)
            _generationTracker.MarkProcessing(certificationProcessId);

            // Fire-and-forget: run in a new DI scope so DbContext survives after request ends
            var specificStepIds = request?.SpecificStepIds;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();

                    // Initialize session context in the new scope
                    var scopedAccessor = scope.ServiceProvider.GetRequiredService<ISessionContextAccessor>();
                    if (tenantContext != null && principalContext != null && securityContext != null)
                    {
                        scopedAccessor.Initialize(tenantContext, principalContext, securityContext, "background-ai-gen");
                    }

                    var scopedEngine = scope.ServiceProvider.GetRequiredService<IRecommendationEngine>();
                    await scopedEngine.GenerateRecommendationsAsync(
                        certificationProcessId,
                        triggeredBy,
                        forceRegenerate,
                        specificStepIds);

                    _logger.LogInformation(
                        "Background AI recommendation generation completed for campaign {CertificationProcessId}",
                        certificationProcessId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Background AI recommendation generation failed for campaign {CertificationProcessId}",
                        certificationProcessId);
                }
                finally
                {
                    _generationTracker.MarkComplete(certificationProcessId);
                }
            });

            return Accepted(processingSummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error triggering AI recommendation generation for campaign {CertificationProcessId}",
                certificationProcessId);
            return StatusCode(500, new { message = "Error generating recommendations", error = ex.Message });
        }
    }

    /// <summary>
    /// Records reviewer feedback on a specific AI recommendation.
    /// </summary>
    /// <param name="recommendationId">The AI recommendation ID.</param>
    /// <param name="request">The feedback data.</param>
    [HttpPost("{recommendationId:guid}/feedback")]
    public async Task<IActionResult> RecordFeedback(
        Guid recommendationId,
        [FromBody] AIRecommendationFeedbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ActualDecision))
        {
            return BadRequest(new { message = "ActualDecision is required" });
        }

        var reviewerUserId = _sessionContext.PrincipalContext?.Id ?? Guid.Empty;
        if (reviewerUserId == Guid.Empty)
        {
            return Unauthorized(new { message = "Reviewer user ID could not be determined" });
        }

        try
        {
            await _aiRecommendationService.RecordFeedbackAsync(
                recommendationId, reviewerUserId, request);

            return Ok(new { message = "Feedback recorded successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording feedback for recommendation {RecommendationId}", recommendationId);
            return StatusCode(500, new { message = "Error recording feedback", error = ex.Message });
        }
    }

    /// <summary>
    /// Checks if AI recommendations exist for a campaign.
    /// </summary>
    /// <param name="certificationProcessId">The campaign ID.</param>
    [HttpGet("{certificationProcessId:long}/exists")]
    public async Task<IActionResult> HasRecommendations(long certificationProcessId)
    {
        var exists = await _aiRecommendationService.HasRecommendationsAsync(certificationProcessId);
        return Ok(new { exists });
    }

    /// <summary>
    /// Interactive reviewer assistant chat endpoint (UC5: Conversational Agent).
    /// Allows reviewers to ask questions about a specific review item.
    /// The assistant uses AI tools to investigate and provide evidence-based answers.
    /// </summary>
    /// <param name="certificationProcessId">The campaign ID.</param>
    /// <param name="stepId">The review item step ID.</param>
    /// <param name="request">The chat request with the reviewer's question.</param>
    [HttpPost("{certificationProcessId:long}/step/{stepId:long}/chat")]
    public async Task<IActionResult> ChatWithAssistant(
        long certificationProcessId,
        long stepId,
        [FromBody] ReviewerChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { message = "Question is required" });
        }

        // Ensure IDs in the request match the URL parameters
        request.CertificationProcessId = certificationProcessId;
        request.ReviewItemStepId = stepId;

        _logger.LogInformation(
            "Reviewer chat request for campaign {CertProcessId}, step {StepId}: {Question}",
            certificationProcessId, stepId, request.Question);

        try
        {
            var response = await _reviewerAssistant.ChatAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in reviewer assistant chat for campaign {CertProcessId}, step {StepId}",
                certificationProcessId, stepId);
            return StatusCode(500, new { message = "Error processing chat request", error = ex.Message });
        }
    }

    // ───── Feedback Analytics Endpoints (Learning Loop Layer 1) ─────

    /// <summary>
    /// Retrieves aggregate feedback metrics for AI recommendation accuracy monitoring.
    /// </summary>
    /// <param name="riskLevel">Optional filter by risk level (Low/Medium/High/Critical).</param>
    /// <param name="windowDays">Number of days to look back (default: 90).</param>
    [HttpGet("feedback/analytics")]
    public async Task<IActionResult> GetFeedbackAnalytics(
        [FromQuery] string? riskLevel = null,
        [FromQuery] int windowDays = 90)
    {
        try
        {
            var metrics = await _feedbackAnalyticsService.GetMetricsAsync(riskLevel, windowDays);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving feedback analytics");
            return StatusCode(500, new { message = "Error retrieving analytics", error = ex.Message });
        }
    }

    /// <summary>
    /// Compares AI recommendation performance across model/prompt versions.
    /// </summary>
    [HttpGet("feedback/versions")]
    public async Task<IActionResult> GetVersionPerformance()
    {
        try
        {
            var versions = await _feedbackAnalyticsService.GetVersionPerformanceAsync();
            return Ok(versions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving version performance");
            return StatusCode(500, new { message = "Error retrieving version performance", error = ex.Message });
        }
    }

    /// <summary>
    /// Detects concept drift — whether AI accuracy has degraded recently vs historical baseline.
    /// </summary>
    /// <param name="recentDays">Recent window in days (default: 30).</param>
    /// <param name="baselineDays">Baseline window in days (default: 90).</param>
    [HttpGet("feedback/drift")]
    public async Task<IActionResult> DetectDrift(
        [FromQuery] int recentDays = 30,
        [FromQuery] int baselineDays = 90)
    {
        try
        {
            var drift = await _feedbackAnalyticsService.DetectConceptDriftAsync(recentDays, baselineDays);
            return Ok(drift);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting concept drift");
            return StatusCode(500, new { message = "Error detecting drift", error = ex.Message });
        }
    }
}
