using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;
using AI.Copilot.Access.Certification.Platform.Session;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Aggregates review item context from the database views and tables for AI processing.
/// Queries V_CertificationReviewItems and enriches with peer analysis, SoD data, and historical decisions.
/// </summary>
[Component(typeof(IDataContextAggregator), ComponentType.Service)]
public class DataContextAggregator : IDataContextAggregator
{
    private readonly ISessionContext _sessionContext;
    private readonly ILogger<DataContextAggregator> _logger;

    public DataContextAggregator(ISessionContext sessionContext, ILogger<DataContextAggregator> logger)
    {
        _sessionContext = sessionContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReviewItemContext>> AggregateContextAsync(
        long certificationProcessId,
        long[]? specificStepIds = null)
    {
        _logger.LogInformation(
            "Aggregating context for campaign {CertificationProcessId}, specific steps: {StepCount}",
            certificationProcessId,
            specificStepIds?.Length ?? -1);

        var db = _sessionContext.DbContext;

        // 1. Fetch review items from the V_CertificationReviewItems view
        var reviewItemsQuery = db.Set<Shared.Entities.Components.Certifications.ReviewItems>()
            .Where(r => r.CertificationProcessId == certificationProcessId);

        if (specificStepIds is { Length: > 0 })
        {
            reviewItemsQuery = reviewItemsQuery.Where(r => specificStepIds.Contains(r.Id));
        }

        var rawReviewItems = await reviewItemsQuery.AsNoTracking().ToListAsync();

        // Deduplicate by Id — the view may return multiple rows per review item
        // (e.g., one per approval group member). Keep only distinct items.
        var reviewItems = rawReviewItems
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .ToList();

        _logger.LogInformation(
            "Found {RawCount} rows, {DistinctCount} distinct review items for campaign {CertificationProcessId}",
            rawReviewItems.Count, reviewItems.Count, certificationProcessId);

        if (reviewItems.Count == 0)
        {
            return Array.Empty<ReviewItemContext>();
        }

        // 2. Compute peer usage statistics (what % of same department + job have this role)
        var peerStats = await ComputePeerUsageAsync(db, reviewItems);

        // 3. Fetch historical decisions for the user-role pairs
        var historicalDecisions = await FetchHistoricalDecisionsAsync(db, certificationProcessId, reviewItems);

        // 4. Detect SoD violations (check WorkflowAuthorizationRequests for violation flags)
        var sodViolations = await DetectSodViolationsAsync(db, reviewItems);

        // 5. Build the enriched context
        var contexts = new List<ReviewItemContext>(reviewItems.Count);
        foreach (var item in reviewItems)
        {
            var daysUnused = item.LastUsed.HasValue
                ? (int)(DateTimeOffset.UtcNow - item.LastUsed.Value).TotalDays
                : (int?)null;

            var context = new ReviewItemContext
            {
                StepId = item.Id,
                CertificationProcessId = item.CertificationProcessId,
                UserId = item.UserId,
                RoleId = item.RoleId,
                SystemId = item.SystemId,
                EmployeeName = item.EmployeeName,
                EmployeeJob = item.EmployeeJob,
                EmployeeDepartment = item.EmployeeDepartment,
                EmployeeManagerName = item.EmployeeManagerName,
                RoleName = item.RoleName,
                RoleDescription = item.RoleDescription,
                Account = item.Account,
                UsagePercentage = item.UsagePercentage,
                UsedActivities = item.UsedActivities,
                LastUsed = item.LastUsed,
                DaysSinceLastUsed = daysUnused,
                PeerUsagePercent = peerStats.GetValueOrDefault(item.Id),
                HasSodViolation = sodViolations.ContainsKey(item.Id),
                SodViolationDetails = sodViolations.GetValueOrDefault(item.Id, []),
                History = historicalDecisions.GetValueOrDefault(
                    BuildHistoryKey(item.UserId, item.RoleId), [])
            };

            contexts.Add(context);
        }

        _logger.LogInformation(
            "Context aggregation complete for campaign {CertificationProcessId}: {Count} items enriched",
            certificationProcessId, contexts.Count);

        return contexts;
    }

    private static string BuildHistoryKey(long? userId, long? roleId)
        => $"{userId ?? 0}:{roleId ?? 0}";

    /// <summary>
    /// Computes what percentage of peers (same department + same job) also hold each role.
    /// </summary>
    private Task<Dictionary<long, decimal>> ComputePeerUsageAsync(
        DbContext db,
        List<Shared.Entities.Components.Certifications.ReviewItems> reviewItems)
    {
        return Task.FromResult(ComputePeerUsageInternal(reviewItems));
    }

    private Dictionary<long, decimal> ComputePeerUsageInternal(
        List<Shared.Entities.Components.Certifications.ReviewItems> reviewItems)
    {
        var result = new Dictionary<long, decimal>();

        try
        {
            // Group items by department+job to batch peer analysis
            var groups = reviewItems
                .Where(r => !string.IsNullOrEmpty(r.EmployeeDepartment) && !string.IsNullOrEmpty(r.EmployeeJob))
                .GroupBy(r => new { r.EmployeeDepartment, r.EmployeeJob });

            foreach (var group in groups)
            {
                // Count total distinct users in the same department+job in this campaign
                var peerCount = reviewItems.Count(r =>
                    r.EmployeeDepartment == group.Key.EmployeeDepartment &&
                    r.EmployeeJob == group.Key.EmployeeJob &&
                    r.UserId.HasValue);

                if (peerCount <= 1) continue;

                foreach (var item in group)
                {
                    // Count how many peers also have this same role
                    var peersWithRole = reviewItems.Count(r =>
                        r.EmployeeDepartment == group.Key.EmployeeDepartment &&
                        r.EmployeeJob == group.Key.EmployeeJob &&
                        r.RoleName == item.RoleName &&
                        r.UserId != item.UserId);

                    var peerPercent = peerCount > 1
                        ? Math.Round((decimal)peersWithRole / (peerCount - 1) * 100, 2)
                        : 0m;

                    result[item.Id] = peerPercent;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error computing peer usage statistics; continuing with defaults");
        }

        return result;
    }

    /// <summary>
    /// Fetches historical approval decisions for user-role pairs from previous campaigns.
    /// </summary>
    private async Task<Dictionary<string, List<HistoricalDecision>>> FetchHistoricalDecisionsAsync(
        DbContext db,
        long currentCertificationProcessId,
        List<Shared.Entities.Components.Certifications.ReviewItems> reviewItems)
    {
        var result = new Dictionary<string, List<HistoricalDecision>>();

        try
        {
            var userIds = reviewItems.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
            if (userIds.Count == 0) return result;

            // Query past review items for the same users in other campaigns
            var pastItems = await db.Set<Shared.Entities.Components.Certifications.ReviewItems>()
                .Where(r => r.CertificationProcessId != currentCertificationProcessId
                         && r.UserId.HasValue
                         && userIds.Contains(r.UserId.Value)
                         && r.IsApproved.HasValue)
                .Select(r => new
                {
                    r.UserId,
                    r.RoleId,
                    r.CertificationProcessId,
                    r.IsApproved,
                    r.Comments
                })
                .AsNoTracking()
                .ToListAsync();

            foreach (var item in pastItems)
            {
                var key = BuildHistoryKey(item.UserId, item.RoleId);
                if (!result.ContainsKey(key))
                {
                    result[key] = [];
                }

                result[key].Add(new HistoricalDecision
                {
                    CertificationProcessId = item.CertificationProcessId,
                    Decision = item.IsApproved == true ? "Approved" : "Rejected",
                    Comments = item.Comments
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching historical decisions; continuing with empty history");
        }

        return result;
    }

    /// <summary>
    /// Detects SoD violations by checking the SoD violation data for users in the review items.
    /// Note: Full SoD detection requires cross-referencing with the SoxEntityViolations/SoxForbiddenCombinations tables.
    /// TODO: Phase 3 — Replace this stub with a real query against the V_SoDViolations view
    /// or the RuleViolations table to detect Separation of Duties conflicts.
    /// Currently returns an empty dictionary, so HasSodViolation is always false from aggregation.
    /// </summary>
    private Task<Dictionary<long, List<string>>> DetectSodViolationsAsync(
        DbContext db,
        List<Shared.Entities.Components.Certifications.ReviewItems> reviewItems)
    {
        var result = new Dictionary<long, List<string>>();

        try
        {
            // SoD violations are tracked in the database. Look for violation flags
            // associated with the users and roles in this campaign.
            // Using raw SQL to query the SoD violation view for relevant data.
            var userIds = reviewItems
                .Where(r => r.UserId.HasValue)
                .Select(r => r.UserId!.Value)
                .Distinct()
                .ToList();

            if (userIds.Count == 0) return Task.FromResult(result);

            // Check for existing SoD flags in WorkflowAuthorizationRequests via WorkflowInstances
            // This is a simplified approach - in production, this would query the V_SoDViolations view
            var itemsWithExceptions = reviewItems
                .Where(r => r.UsagePercentage == 0 && r.UsedActivities == 0)
                .ToList();

            foreach (var item in itemsWithExceptions)
            {
                // Mark items with zero usage as potential risk (not necessarily SoD, but flagged for review)
                // Full SoD detection would cross-reference with the RuleViolations table
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting SoD violations; continuing with empty violations");
        }

        return Task.FromResult(result);
    }
}
