using AI.Copilot.Access.Certification.Platform.Attributes;
using AI.Copilot.Access.Certification.Platform.Database;
using AI.Copilot.Access.Certification.Platform.Session;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

namespace AI.Copilot.Access.Certification.Components.Certification;

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
