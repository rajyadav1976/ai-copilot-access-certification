using Pathlock.Cloud.Platform.Attributes;
using Pathlock.Cloud.Platform.Database;
using Pathlock.Cloud.Platform.Session;
using Pathlock.Cloud.Shared.Entities.Components.Certifications;

namespace Pathlock.Cloud.Components.Certification;

/// <summary>
/// Repository interface for AIRecommendationFeedback entities.
/// </summary>
public interface IAIRecommendationFeedbackRepository : IRepository<AIRecommendationFeedback> { }

/// <summary>
/// Repository for AI recommendation feedback data access.
/// </summary>
[Component(typeof(IAIRecommendationFeedbackRepository), ComponentType.Repository)]
public class AIRecommendationFeedbackRepository : BaseRepository<AIRecommendationFeedback>, IAIRecommendationFeedbackRepository
{
    public AIRecommendationFeedbackRepository(ISessionContext sessionContext) : base(sessionContext)
    {
    }
}
