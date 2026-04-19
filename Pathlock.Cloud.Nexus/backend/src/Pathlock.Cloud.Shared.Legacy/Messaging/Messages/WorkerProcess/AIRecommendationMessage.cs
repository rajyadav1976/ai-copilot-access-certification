namespace Pathlock.Cloud.Shared.Legacy.Messaging.Messages.WorkerProcess
{
    /// <summary>
    /// Message to trigger AI recommendation generation for a certification campaign.
    /// Sent after campaign statistics are calculated or when the reviewer requests AI analysis.
    /// </summary>
    public class AIRecommendationMessage : BaseMessage
    {
        /// <summary>
        /// The certification process ID to generate recommendations for.
        /// </summary>
        public long CertificationProcessId { get; set; }

        /// <summary>
        /// The manager/user who triggered the recommendation generation.
        /// </summary>
        public string TriggeredBy { get; set; } = string.Empty;

        /// <summary>
        /// Whether to force regeneration even if recommendations already exist.
        /// </summary>
        public bool ForceRegenerate { get; set; }

        /// <summary>
        /// Optional: Specific review item step IDs to generate recommendations for.
        /// If null or empty, recommendations will be generated for all items in the campaign.
        /// </summary>
        public long[]? SpecificStepIds { get; set; }
    }
}
