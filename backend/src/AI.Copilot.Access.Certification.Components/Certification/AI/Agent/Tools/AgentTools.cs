using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

namespace AI.Copilot.Access.Certification.Components.Certification.AI.Agent.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// Tool 1: GetHistoricalDecisions — Retrieves past approval/rejection history
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GetHistoricalDecisionsTool : IAgentTool
{
    public string Name => "get_historical_decisions";
    public string Description =>
        "Retrieves historical approval/rejection decisions for a specific user-role combination from previous certification campaigns. " +
        "Use this when you want to understand if this role assignment was previously approved or rejected.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "userId": { "type": "integer", "description": "The employee user ID" },
                    "roleId": { "type": "integer", "description": "The role ID to check history for" }
                },
                "required": ["userId", "roleId"]
            }
            """).RootElement
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var userId = arguments.GetProperty("userId").GetInt64();
        var roleId = arguments.GetProperty("roleId").GetInt64();

        var history = await context.DbContext.Set<ReviewItems>()
            .Where(r => r.UserId == userId && r.RoleId == roleId
                     && r.CertificationProcessId != context.CertificationProcessId
                     && r.IsApproved.HasValue)
            .Select(r => new { r.CertificationProcessId, r.IsApproved, r.Comments })
            .AsNoTracking()
            .Take(10)
            .ToListAsync(ct);

        if (history.Count == 0)
            return "No historical decisions found for this user-role combination. This appears to be the first time this assignment is being reviewed.";

        var lines = history.Select(h =>
            $"- Campaign {h.CertificationProcessId}: {(h.IsApproved == true ? "Approved" : "Rejected")}" +
            (string.IsNullOrEmpty(h.Comments) ? "" : $" (Comment: {h.Comments})"));

        return $"Found {history.Count} historical decisions:\n{string.Join('\n', lines)}";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 2: CheckSoDViolations — Checks Separation of Duties violations
// ─────────────────────────────────────────────────────────────────────────────
public sealed class CheckSoDViolationsTool : IAgentTool
{
    public string Name => "check_sod_violations";
    public string Description =>
        "Checks for Separation of Duties (SoD) violations for a given user. " +
        "Returns details about conflicting role combinations that violate business rules. " +
        "Use this when the user has suspicious role combinations or when you need to verify compliance.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "userId": { "type": "integer", "description": "The employee user ID to check for SoD violations" }
                },
                "required": ["userId"]
            }
            """).RootElement
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var userId = arguments.GetProperty("userId").GetInt64();

        // Check for other roles this user has in the same campaign that might conflict
        var userRoles = context.AllItems?
            .Where(i => i.UserId == userId)
            .Select(i => new { i.RoleName, i.UsagePercentage, i.HasSodViolation, i.SodViolationDetails })
            .ToList();

        if (userRoles == null || userRoles.Count == 0)
            return "No role data available for this user in the current campaign.";

        var violations = userRoles.Where(r => r.HasSodViolation).ToList();
        var allRoleNames = string.Join(", ", userRoles.Select(r => r.RoleName ?? "Unknown"));

        var result = $"User has {userRoles.Count} roles in this campaign: {allRoleNames}\n";

        if (violations.Count > 0)
        {
            result += $"\nSoD VIOLATIONS DETECTED ({violations.Count}):\n";
            foreach (var v in violations)
            {
                result += $"- Role '{v.RoleName}' has SoD violation";
                if (v.SodViolationDetails.Count > 0)
                    result += $": {string.Join("; ", v.SodViolationDetails)}";
                result += "\n";
            }
        }
        else
        {
            result += "\nNo SoD violations detected for this user's current role assignments.";
        }

        return result;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 3: GetPeerGroupDetails — Analyzes peer group role patterns
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GetPeerGroupDetailsTool : IAgentTool
{
    public string Name => "get_peer_group_details";
    public string Description =>
        "Retrieves detailed peer group analysis for a specific department and job combination. " +
        "Shows which roles peers hold, their usage patterns, and whether the current role is common or unusual among peers. " +
        "Use this when peer usage is low or when you need to understand the norm for this job function.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "department": { "type": "string", "description": "The department name" },
                    "jobTitle": { "type": "string", "description": "The job title" },
                    "roleName": { "type": "string", "description": "The role name to check peer adoption for" }
                },
                "required": ["department", "jobTitle", "roleName"]
            }
            """).RootElement
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var department = arguments.GetProperty("department").GetString() ?? "";
        var jobTitle = arguments.GetProperty("jobTitle").GetString() ?? "";
        var roleName = arguments.GetProperty("roleName").GetString() ?? "";

        var peers = context.AllItems?
            .Where(i => string.Equals(i.EmployeeDepartment, department, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(i.EmployeeJob, jobTitle, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        if (peers.Count == 0)
            return Task.FromResult($"No peers found in department '{department}' with job '{jobTitle}'.");

        var distinctUsers = peers.Select(p => p.UserId).Distinct().Count();
        var peersWithRole = peers.Where(p => string.Equals(p.RoleName, roleName, StringComparison.OrdinalIgnoreCase)).ToList();
        var peerPercent = distinctUsers > 0 ? (decimal)peersWithRole.Select(p => p.UserId).Distinct().Count() / distinctUsers * 100 : 0;

        // Find top roles in this peer group
        var topRoles = peers
            .GroupBy(p => p.RoleName ?? "Unknown")
            .OrderByDescending(g => g.Select(x => x.UserId).Distinct().Count())
            .Take(10)
            .Select(g => new
            {
                Role = g.Key,
                UserCount = g.Select(x => x.UserId).Distinct().Count(),
                AvgUsage = (int)g.Average(x => x.UsagePercentage)
            })
            .ToList();

        var result = $"Peer Group: {department} / {jobTitle}\n" +
                     $"Total distinct users: {distinctUsers}\n" +
                     $"Users with role '{roleName}': {peersWithRole.Select(p => p.UserId).Distinct().Count()} ({peerPercent:F1}%)\n\n" +
                     $"Top roles in this peer group:\n";

        foreach (var r in topRoles)
        {
            var marker = string.Equals(r.Role, roleName, StringComparison.OrdinalIgnoreCase) ? " ← THIS ROLE" : "";
            result += $"  - {r.Role}: {r.UserCount} users, avg usage {r.AvgUsage}%{marker}\n";
        }

        if (peersWithRole.Count > 0)
        {
            var avgPeerUsage = (int)peersWithRole.Average(p => p.UsagePercentage);
            result += $"\nPeers with this role have avg usage: {avgPeerUsage}%";
        }

        return Task.FromResult(result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 4: GetRoleRiskProfile — Retrieves role metadata and risk classification
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GetRoleRiskProfileTool : IAgentTool
{
    public string Name => "get_role_risk_profile";
    public string Description =>
        "Analyzes a role's risk profile based on its name, description, and usage patterns across the campaign. " +
        "Identifies privileged/sensitive roles (e.g., SAP_ALL, ADMIN roles) and provides aggregate statistics. " +
        "Use this when reviewing a role you're unfamiliar with or that appears potentially sensitive.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "roleName": { "type": "string", "description": "The role name to analyze" }
                },
                "required": ["roleName"]
            }
            """).RootElement
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var roleName = arguments.GetProperty("roleName").GetString() ?? "";

        // Analyze across all campaign items
        var itemsWithRole = context.AllItems?
            .Where(i => string.Equals(i.RoleName, roleName, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        if (itemsWithRole.Count == 0)
            return Task.FromResult($"Role '{roleName}' was not found in the current campaign data.");

        var first = itemsWithRole[0];
        var userCount = itemsWithRole.Select(i => i.UserId).Distinct().Count();
        var avgUsage = (int)itemsWithRole.Average(i => i.UsagePercentage);
        var zeroUsageCount = itemsWithRole.Count(i => i.UsagePercentage == 0);
        var sodCount = itemsWithRole.Count(i => i.HasSodViolation);

        // Heuristic: check if role name suggests privilege
        var upperName = roleName.ToUpperInvariant();
        var isPrivileged = upperName.Contains("SAP_ALL") || upperName.Contains("ADMIN") ||
                          upperName.Contains("SUPER") || upperName.Contains("ROOT") ||
                          upperName.Contains("FULL_ACCESS") || upperName.Contains("EMERGENCY") ||
                          upperName.Contains("BREAK_GLASS") || upperName.Contains("DEBUG");

        var result = $"Role: {roleName}\n" +
                     $"Description: {first.RoleDescription ?? "No description available"}\n" +
                     $"Assigned to: {userCount} users in this campaign\n" +
                     $"Average usage: {avgUsage}%\n" +
                     $"Zero-usage assignments: {zeroUsageCount} ({(itemsWithRole.Count > 0 ? zeroUsageCount * 100 / itemsWithRole.Count : 0)}%)\n" +
                     $"SoD violations: {sodCount}\n" +
                     $"Privileged role indicator: {(isPrivileged ? "YES — role name suggests elevated privileges" : "No")}\n";

        // Check anomaly scores if available
        if (context.AnomalyScores != null)
        {
            var scores = itemsWithRole
                .Where(i => context.AnomalyScores.ContainsKey(i.StepId))
                .Select(i => context.AnomalyScores[i.StepId])
                .ToList();

            if (scores.Count > 0)
            {
                result += $"Anomaly scores: avg={scores.Average():F3}, max={scores.Max():F3}, " +
                          $"flagged={scores.Count(s => s >= 0.65m)}/{scores.Count}\n";
            }
        }

        return Task.FromResult(result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 5: GetCampaignOverview — Summary stats for the campaign
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GetCampaignOverviewTool : IAgentTool
{
    public string Name => "get_campaign_overview";
    public string Description =>
        "Retrieves high-level statistics for the entire certification campaign. " +
        "Shows total items, user/role distributions, risk breakdowns, and aggregate metrics. " +
        "Use this for campaign-level context or when you need to understand the broader picture.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {},
                "required": []
            }
            """).RootElement
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var items = context.AllItems ?? [];

        if (items.Count == 0)
            return Task.FromResult("No items available for this campaign.");

        var totalUsers = items.Select(i => i.UserId).Distinct().Count();
        var totalRoles = items.Select(i => i.RoleName).Distinct().Count();
        var avgUsage = (int)items.Average(i => i.UsagePercentage);
        var zeroUsage = items.Count(i => i.UsagePercentage == 0);
        var sodViolations = items.Count(i => i.HasSodViolation);
        var neverUsed = items.Count(i => !i.LastUsed.HasValue);
        var staleAccess = items.Count(i => i.DaysSinceLastUsed is > 180);

        var deptBreakdown = items
            .GroupBy(i => i.EmployeeDepartment ?? "Unknown")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"  - {g.Key}: {g.Count()} items, {g.Select(x => x.UserId).Distinct().Count()} users");

        var result = $"Campaign {context.CertificationProcessId} Overview:\n" +
                     $"Total items: {items.Count}\n" +
                     $"Distinct users: {totalUsers}\n" +
                     $"Distinct roles: {totalRoles}\n" +
                     $"Average usage: {avgUsage}%\n" +
                     $"Zero-usage items: {zeroUsage} ({zeroUsage * 100 / items.Count}%)\n" +
                     $"Never-used items: {neverUsed}\n" +
                     $"Stale access (>180 days): {staleAccess}\n" +
                     $"SoD violations: {sodViolations}\n\n" +
                     $"Top departments:\n{string.Join('\n', deptBreakdown)}\n";

        return Task.FromResult(result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 6: DetectBulkPatterns — Identifies suspicious bulk provisioning patterns
// ─────────────────────────────────────────────────────────────────────────────
public sealed class DetectBulkPatternsTool : IAgentTool
{
    public string Name => "detect_bulk_patterns";
    public string Description =>
        "Scans the campaign for unusual bulk provisioning patterns, such as many users receiving the same role simultaneously, " +
        "roles with universally zero usage, or departments with abnormal role distributions. " +
        "Use this for campaign-level risk analysis.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {},
                "required": []
            }
            """).RootElement
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var items = context.AllItems ?? [];
        if (items.Count == 0)
            return Task.FromResult("No items available for pattern detection.");

        var patterns = new List<string>();

        // Pattern 1: Roles assigned to many users but with universally zero usage
        var roleGroups = items.GroupBy(i => i.RoleName ?? "Unknown").ToList();
        foreach (var group in roleGroups.Where(g => g.Count() >= 3))
        {
            var zeroUsagePercent = (decimal)group.Count(i => i.UsagePercentage == 0) / group.Count() * 100;
            if (zeroUsagePercent >= 80)
            {
                patterns.Add($"UNUSED BULK ROLE: '{group.Key}' assigned to {group.Count()} users, " +
                             $"{zeroUsagePercent:F0}% have zero usage — potential over-provisioning");
            }
        }

        // Pattern 2: Users with an unusually high number of roles
        var userRoleCounts = items.GroupBy(i => i.UserId)
            .Select(g => new { UserId = g.Key, Name = g.First().EmployeeName, Count = g.Count() })
            .OrderByDescending(u => u.Count)
            .Take(5)
            .ToList();

        var avgRolesPerUser = items.GroupBy(i => i.UserId).Average(g => g.Count());
        foreach (var user in userRoleCounts.Where(u => u.Count > avgRolesPerUser * 2))
        {
            patterns.Add($"HIGH ROLE COUNT: User '{user.Name}' (ID: {user.UserId}) has {user.Count} roles " +
                         $"(campaign avg: {avgRolesPerUser:F1}) — potential privilege accumulation");
        }

        // Pattern 3: Roles with SoD violations across multiple users
        var sodRoles = items.Where(i => i.HasSodViolation)
            .GroupBy(i => i.RoleName ?? "Unknown")
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in sodRoles)
        {
            patterns.Add($"WIDESPREAD SOD: Role '{group.Key}' has SoD violations for {group.Count()} users — systemic policy conflict");
        }

        // Pattern 4: Departments with disproportionately high zero-usage
        var deptStats = items.GroupBy(i => i.EmployeeDepartment ?? "Unknown")
            .Where(g => g.Count() >= 5)
            .Select(g => new
            {
                Dept = g.Key,
                Total = g.Count(),
                ZeroUsage = g.Count(i => i.UsagePercentage == 0),
                ZeroPercent = (decimal)g.Count(i => i.UsagePercentage == 0) / g.Count() * 100
            })
            .Where(d => d.ZeroPercent > 70)
            .ToList();

        foreach (var dept in deptStats)
        {
            patterns.Add($"DEPT ZERO USAGE: Department '{dept.Dept}' has {dept.ZeroPercent:F0}% zero-usage items " +
                         $"({dept.ZeroUsage}/{dept.Total}) — possible stale provisioning");
        }

        return Task.FromResult(patterns.Count == 0
            ? "No significant bulk patterns or anomalies detected in this campaign."
            : $"Detected {patterns.Count} bulk patterns:\n\n{string.Join("\n\n", patterns)}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 7: GetSimilarPastDecisions — Finds similar items from prior campaigns
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GetSimilarPastDecisionsTool : IAgentTool
{
    public string Name => "get_similar_past_decisions";
    public string Description =>
        "Finds similar review items from past campaigns and their outcomes. Matches by role name, usage range, and risk profile. " +
        "Use this to calibrate your confidence by comparing against historical reviewer decisions for similar situations.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "roleName": { "type": "string", "description": "The role name to find similar items for" },
                    "usagePercentage": { "type": "integer", "description": "Current usage percentage to match similar ranges" },
                    "hasSodViolation": { "type": "boolean", "description": "Whether the item has SoD violations" }
                },
                "required": ["roleName", "usagePercentage"]
            }
            """).RootElement
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var roleName = arguments.GetProperty("roleName").GetString() ?? "";
        var usage = arguments.GetProperty("usagePercentage").GetInt64();
        var hasSod = arguments.TryGetProperty("hasSodViolation", out var sodVal) && sodVal.GetBoolean();

        // Define a usage range for "similar" (±20%)
        var usageLow = Math.Max(0, usage - 20);
        var usageHigh = Math.Min(100, usage + 20);

        // Find similar items from past campaigns
        var pastItems = await context.DbContext.Set<ReviewItems>()
            .Where(r => r.CertificationProcessId != context.CertificationProcessId
                     && r.RoleName == roleName
                     && r.UsagePercentage >= usageLow
                     && r.UsagePercentage <= usageHigh
                     && r.IsApproved.HasValue)
            .Select(r => new { r.IsApproved, r.UsagePercentage, r.Comments })
            .AsNoTracking()
            .Take(20)
            .ToListAsync(ct);

        if (pastItems.Count == 0)
        {
            // Broaden search: same role, any usage
            pastItems = await context.DbContext.Set<ReviewItems>()
                .Where(r => r.CertificationProcessId != context.CertificationProcessId
                         && r.RoleName == roleName
                         && r.IsApproved.HasValue)
                .Select(r => new { r.IsApproved, r.UsagePercentage, r.Comments })
                .AsNoTracking()
                .Take(20)
                .ToListAsync(ct);
        }

        if (pastItems.Count == 0)
            return $"No historical decisions found for role '{roleName}'. This may be a new role or first-time review.";

        var approved = pastItems.Count(p => p.IsApproved == true);
        var rejected = pastItems.Count(p => p.IsApproved == false);
        var approvalRate = (decimal)approved / pastItems.Count * 100;

        var result = $"Historical decisions for similar items (role: '{roleName}', usage: {usageLow}-{usageHigh}%):\n" +
                     $"Total similar items found: {pastItems.Count}\n" +
                     $"Approved: {approved} ({approvalRate:F0}%)\n" +
                     $"Rejected: {rejected} ({100 - approvalRate:F0}%)\n";

        // Include reviewer comments if available
        var comments = pastItems.Where(p => !string.IsNullOrEmpty(p.Comments)).Take(3).ToList();
        if (comments.Count > 0)
        {
            result += "\nSample reviewer comments:\n";
            foreach (var c in comments)
            {
                result += $"  - {(c.IsApproved == true ? "Approved" : "Rejected")} (usage: {c.UsagePercentage}%): {c.Comments}\n";
            }
        }

        result += $"\nHistorical approval rate: {approvalRate:F0}%. ";
        if (approvalRate >= 80)
            result += "Reviewers overwhelmingly approve this type of access.";
        else if (approvalRate <= 20)
            result += "Reviewers overwhelmingly reject this type of access.";
        else
            result += "Reviewers are split on this type of access — recommend NeedsReview if uncertain.";

        return result;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 8: SimulateApprovalImpact — Checks consequences of approval
// ─────────────────────────────────────────────────────────────────────────────
public sealed class SimulateApprovalImpactTool : IAgentTool
{
    public string Name => "simulate_approval_impact";
    public string Description =>
        "Simulates what would happen if the current role assignment is approved. " +
        "Checks for potential new SoD conflicts, role redundancy, and access concentration risks. " +
        "Use this to understand the downstream impact of approving a particular role.";

    public AgentToolDefinition GetDefinition() => new()
    {
        Function = new AgentFunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "userId": { "type": "integer", "description": "The employee user ID" },
                    "roleName": { "type": "string", "description": "The role name being considered" }
                },
                "required": ["userId", "roleName"]
            }
            """).RootElement
        }
    };

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var userId = arguments.GetProperty("userId").GetInt64();
        var roleName = arguments.GetProperty("roleName").GetString() ?? "";

        var userItems = context.AllItems?
            .Where(i => i.UserId == userId)
            .ToList() ?? [];

        if (userItems.Count == 0)
            return Task.FromResult("No data available for this user in the campaign.");

        var currentItem = userItems.FirstOrDefault(i =>
            string.Equals(i.RoleName, roleName, StringComparison.OrdinalIgnoreCase));

        var otherRoles = userItems.Where(i =>
            !string.Equals(i.RoleName, roleName, StringComparison.OrdinalIgnoreCase)).ToList();

        var impacts = new List<string>();

        // Check: total role count if approved
        var totalRoles = userItems.Count;
        var avgRolesInCampaign = context.AllItems?
            .GroupBy(i => i.UserId)
            .Average(g => g.Count()) ?? 0;

        if (totalRoles > avgRolesInCampaign * 1.5)
        {
            impacts.Add($"ACCESS CONCENTRATION: User would retain {totalRoles} roles " +
                       $"(campaign avg: {avgRolesInCampaign:F1}). High role count increases attack surface.");
        }

        // Check: SoD with other roles
        var existingSodRoles = otherRoles.Where(r => r.HasSodViolation).ToList();
        if (existingSodRoles.Count > 0)
        {
            impacts.Add($"EXISTING SOD CONFLICTS: User already has {existingSodRoles.Count} other roles with SoD violations: " +
                       string.Join(", ", existingSodRoles.Select(r => r.RoleName)));
        }

        if (currentItem?.HasSodViolation == true)
        {
            impacts.Add($"THIS ROLE HAS SOD VIOLATION: Approving '{roleName}' maintains an active Separation of Duties conflict.");
        }

        // Check: redundancy with low-usage roles
        var unusedRoles = otherRoles.Where(r => r.UsagePercentage == 0).ToList();
        if (unusedRoles.Count > 0)
        {
            impacts.Add($"UNUSED ACCESS: User also has {unusedRoles.Count} other roles with zero usage: " +
                       string.Join(", ", unusedRoles.Select(r => r.RoleName).Take(5)) +
                       ". Consider reviewing the entire access portfolio.");
        }

        // Check: privileged role names
        var upperName = roleName.ToUpperInvariant();
        if (upperName.Contains("SAP_ALL") || upperName.Contains("ADMIN") || upperName.Contains("SUPER") ||
            upperName.Contains("FULL_ACCESS") || upperName.Contains("EMERGENCY"))
        {
            impacts.Add($"PRIVILEGED ROLE: '{roleName}' appears to be a privileged/administrative role. " +
                       "Approving grants elevated access that should be time-limited or closely monitored.");
        }

        if (impacts.Count == 0)
        {
            return Task.FromResult($"Approval impact for '{roleName}' for user (ID: {userId}):\n" +
                                  "No significant additional risks identified. " +
                                  "The user's access portfolio appears reasonable for their role.");
        }

        return Task.FromResult($"Approval impact for '{roleName}' for user (ID: {userId}):\n\n" +
                              string.Join("\n\n", impacts));
    }
}
