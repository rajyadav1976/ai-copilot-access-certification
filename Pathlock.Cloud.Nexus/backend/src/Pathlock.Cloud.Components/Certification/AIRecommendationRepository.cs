using Pathlock.Cloud.Platform.Attributes;
using Pathlock.Cloud.Platform.Database;
using Pathlock.Cloud.Platform.Session;
using Pathlock.Cloud.Shared.Entities.Components.Certifications;

namespace Pathlock.Cloud.Components.Certification;

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
