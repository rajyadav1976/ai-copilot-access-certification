using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;
using AI.Copilot.Access.Certification.Platform.Session;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Service for retrieving, summarizing, and recording feedback on AI recommendations.
/// </summary>
[Component(typeof(IAIRecommendationService), ComponentType.Service)]
public class AIRecommendationService : IAIRecommendationService
{
    private readonly ISessionContext _sessionContext;
    private readonly ILogger<AIRecommendationService> _logger;

    public AIRecommendationService(ISessionContext sessionContext, ILogger<AIRecommendationService> logger)
    {
        _sessionContext = sessionContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AIRecommendationDto>> GetRecommendationsAsync(
        long certificationProcessId,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        var recommendations = await db.Set<AIRecommendation>()
            .Where(r => r.CertificationProcessId == certificationProcessId
                        && (r.Status == "Generated" || r.Status == "Failed"))
            .AsNoTracking()
            .OrderBy(r => r.ReviewItemStepId)
            .ToListAsync(cancellationToken);

        var dtos = recommendations.Select(MapToDto).ToList();

        // Enrich with display fields from the review items view
        await EnrichWithReviewItemDataAsync(db, certificationProcessId, dtos, cancellationToken);

        return dtos;
    }

    public async Task<AIRecommendationDto?> GetRecommendationByStepIdAsync(
        long certificationProcessId,
        long reviewItemStepId,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        var recommendation = await db.Set<AIRecommendation>()
            .Where(r => r.CertificationProcessId == certificationProcessId
                      && r.ReviewItemStepId == reviewItemStepId
                      && (r.Status == "Generated" || r.Status == "Failed"))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (recommendation == null) return null;

        var dto = MapToDto(recommendation);

        // Enrich with display fields from the review items view
        await EnrichWithReviewItemDataAsync(db, certificationProcessId, new List<AIRecommendationDto> { dto }, cancellationToken);

        return dto;
    }

    public async Task<AIRecommendationSummary> GetRecommendationSummaryAsync(
        long certificationProcessId,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        var recommendations = await db.Set<AIRecommendation>()
            .Where(r => r.CertificationProcessId == certificationProcessId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (recommendations.Count == 0)
        {
            return new AIRecommendationSummary
            {
                CertificationProcessId = certificationProcessId,
                Status = "NotGenerated"
            };
        }

        var generated = recommendations.Where(r => r.Status == "Generated").ToList();
        var failed = recommendations.Count(r => r.Status == "Failed");
        var pending = recommendations.Any(r => r.Status == "Pending");

        // Determine overall status correctly:
        // - ALL failed → "Failed"
        // - Some pending → "Processing"
        // - Some failed + some generated → "Generated" (partial success)
        // - Otherwise → "Generated"
        string status;
        if (generated.Count == 0 && failed > 0)
            status = "Failed";
        else if (pending)
            status = "Processing";
        else
            status = "Generated";

        return new AIRecommendationSummary
        {
            CertificationProcessId = certificationProcessId,
            TotalItems = recommendations.Count,
            RecommendedApprove = generated.Count(r => r.Decision == "Approve"),
            RecommendedReject = generated.Count(r => r.Decision == "Reject"),
            NeedsReview = generated.Count(r => r.Decision == "NeedsReview"),
            HighRiskCount = generated.Count(r => r.RiskLevel is "High" or "Critical"),
            AnomalyCount = generated.Count(r => r.IsAnomaly),
            SodViolationCount = generated.Count(r => r.HasSodViolation),
            AverageConfidence = generated.Count > 0
                ? Math.Round(generated.Average(r => r.ConfidenceScore), 4)
                : 0m,
            FailedCount = failed,
            Status = status,
            GeneratedAt = generated.MaxBy(r => r.GeneratedAt)?.GeneratedAt
        };
    }

    public async Task RecordFeedbackAsync(
        Guid recommendationId,
        Guid reviewerUserId,
        AIRecommendationFeedbackRequest feedback,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        // Verify the recommendation exists
        var recommendation = await db.Set<AIRecommendation>()
            .FirstOrDefaultAsync(r => r.SysId == recommendationId, cancellationToken);

        if (recommendation == null)
        {
            throw new InvalidOperationException(
                $"AI recommendation with ID {recommendationId} not found");
        }

        // Check if feedback already exists for this reviewer
        var existingFeedback = await db.Set<AIRecommendationFeedback>()
            .FirstOrDefaultAsync(f => f.AIRecommendationId == recommendationId
                                   && f.ReviewerUserId == reviewerUserId,
                cancellationToken);

        if (existingFeedback != null)
        {
            // Update existing feedback
            existingFeedback.ActualDecision = feedback.ActualDecision;
            existingFeedback.AgreedWithAI = feedback.AgreedWithAI;
            existingFeedback.OverrideReason = feedback.OverrideReason;
            existingFeedback.FeedbackComments = feedback.FeedbackComments;
            existingFeedback.QualityRating = feedback.QualityRating;
            existingFeedback.FeedbackTimestamp = DateTimeOffset.UtcNow;

            db.Set<AIRecommendationFeedback>().Update(existingFeedback);
        }
        else
        {
            // Create new feedback
            var newFeedback = new AIRecommendationFeedback
            {
                SysId = Guid.NewGuid(),
                AIRecommendationId = recommendationId,
                ReviewerUserId = reviewerUserId,
                ActualDecision = feedback.ActualDecision,
                AgreedWithAI = feedback.AgreedWithAI,
                OverrideReason = feedback.OverrideReason,
                FeedbackComments = feedback.FeedbackComments,
                QualityRating = feedback.QualityRating,
                FeedbackTimestamp = DateTimeOffset.UtcNow,
                CreatedBy = reviewerUserId.ToString()
            };

            await db.Set<AIRecommendationFeedback>().AddAsync(newFeedback, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Feedback recorded for recommendation {RecommendationId} by reviewer {ReviewerUserId}. " +
            "Agreed: {AgreedWithAI}, Decision: {Decision}",
            recommendationId, reviewerUserId, feedback.AgreedWithAI, feedback.ActualDecision);
    }

    public async Task<bool> HasRecommendationsAsync(
        long certificationProcessId,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        return await db.Set<AIRecommendation>()
            .AnyAsync(r => r.CertificationProcessId == certificationProcessId
                        && r.Status == "Generated",
                cancellationToken);
    }

    private static AIRecommendationDto MapToDto(AIRecommendation entity)
    {
        return new AIRecommendationDto
        {
            Id = entity.SysId ?? Guid.Empty,
            CertificationProcessId = entity.CertificationProcessId,
            ReviewItemStepId = entity.ReviewItemStepId,
            UserId = entity.UserId,
            RoleId = entity.RoleId,
            Decision = entity.Decision,
            ConfidenceScore = entity.ConfidenceScore,
            RiskLevel = entity.RiskLevel,
            RiskSummary = entity.RiskSummary,
            RiskFactors = DeserializeRiskFactors(entity.RiskFactors),
            UsagePercentage = entity.UsagePercentage,
            DaysSinceLastUsed = entity.DaysSinceLastUsed,
            HasSodViolation = entity.HasSodViolation,
            PeerUsagePercent = entity.PeerUsagePercent,
            IsAnomaly = entity.IsAnomaly,
            AnomalyScore = entity.AnomalyScore,
            Status = entity.Status,
            ErrorMessage = entity.ErrorMessage,
            GeneratedAt = entity.GeneratedAt
        };
    }

    private static List<string> DeserializeRiskFactors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task EnrichWithReviewItemDataAsync(
        DbContext db,
        long certificationProcessId,
        List<AIRecommendationDto> dtos,
        CancellationToken cancellationToken)
    {
        try
        {
            var stepIds = dtos.Select(d => d.ReviewItemStepId).ToList();
            // Use projection to avoid type-mismatch errors on columns we don't need
            var reviewItems = await db.Set<ReviewItems>()
                .Where(r => r.CertificationProcessId == certificationProcessId && stepIds.Contains(r.Id))
                .AsNoTracking()
                .Select(r => new
                {
                    r.Id,
                    r.RoleName,
                    r.RoleDescription,
                    r.EmployeeName,
                    r.EmployeeJob,
                    r.EmployeeDepartment,
                    r.Account
                })
                .ToListAsync(cancellationToken);

            var lookup = reviewItems
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var dto in dtos)
            {
                if (lookup.TryGetValue(dto.ReviewItemStepId, out var item))
                {
                    dto.RoleName = item.RoleName;
                    dto.RoleDescription = item.RoleDescription;
                    dto.EmployeeName = item.EmployeeName;
                    dto.EmployeeJob = item.EmployeeJob;
                    dto.EmployeeDepartment = item.EmployeeDepartment;
                    dto.Account = item.Account;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException)
        {
            _logger.LogDebug(ex, "ReviewItems enrichment unavailable; skipping");
        }
    }
}
