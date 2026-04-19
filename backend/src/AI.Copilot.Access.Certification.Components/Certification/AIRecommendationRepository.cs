using AI.Copilot.Access.Certification.Platform.Attributes;
using AI.Copilot.Access.Certification.Platform.Database;
using AI.Copilot.Access.Certification.Platform.Session;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

namespace AI.Copilot.Access.Certification.Components.Certification;

/// <summary>
/// Repository interface for AIRecommendation entities.
/// </summary>
public interface IAIRecommendationRepository : IRepository<AIRecommendation> { }

/// <summary>
/// Repository for AI recommendation data access.
/// Uses BaseRepository directly (not ApproverFilteredRepository) because recommendations
/// are campaign-level, not approver-filtered.
/// </summary>
[Component(typeof(IAIRecommendationRepository), ComponentType.Repository)]
public class AIRecommendationRepository : BaseRepository<AIRecommendation>, IAIRecommendationRepository
{
    public AIRecommendationRepository(ISessionContext sessionContext) : base(sessionContext)
    {
    }
}
