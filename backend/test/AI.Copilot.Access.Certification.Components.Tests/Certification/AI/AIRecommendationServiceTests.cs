using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;
using AI.Copilot.Access.Certification.Components.Certification.AI;
using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Database;
using AI.Copilot.Access.Certification.Platform.Session;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;
using Xunit;

namespace AI.Copilot.Access.Certification.Components.Tests.Certification.AI;

/// <summary>
/// Test DbContext for AI recommendation service tests.
/// Includes the DbSet properties needed by the service.
/// </summary>
public class AIServiceTestDbContext(DbContextOptions o) : BaseDbContext(o)
{
    public DbSet<AIRecommendation> AIRecommendations { get; set; } = null!;
    public DbSet<AIRecommendationFeedback> AIRecommendationFeedbacks { get; set; } = null!;
}

public class AIRecommendationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AIServiceTestDbContext _dbContext;
    private readonly Mock<ISessionContext> _sessionContextMock;
    private readonly Mock<ILogger<AIRecommendationService>> _loggerMock;
    private readonly AIRecommendationService _service;

    public AIRecommendationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AIServiceTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new AIServiceTestDbContext(options);
        _dbContext.Database.EnsureCreated();

        _sessionContextMock = new Mock<ISessionContext>();
        _sessionContextMock.Setup(s => s.DbContext).Returns(_dbContext);

        _loggerMock = new Mock<ILogger<AIRecommendationService>>();

        _service = new AIRecommendationService(
            _sessionContextMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetRecommendationsAsync_ReturnsFilteredResults()
    {
        // Arrange
        var certificationProcessId = 100L;
        await SeedRecommendation(certificationProcessId, 1, "Approve", 0.9m, "Low");
        await SeedRecommendation(certificationProcessId, 2, "Reject", 0.85m, "High");
        await SeedRecommendation(certificationProcessId, 3, "NeedsReview", 0.6m, "Medium");

        // Act
        var results = await _service.GetRecommendationsAsync(certificationProcessId);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GetRecommendationByStepIdAsync_ReturnsCorrectItem()
    {
        // Arrange
        var certificationProcessId = 200L;
        var stepId = 42L;
        await SeedRecommendation(certificationProcessId, stepId, "Reject", 0.88m, "High");

        // Act
        var result = await _service.GetRecommendationByStepIdAsync(certificationProcessId, stepId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(stepId, result.ReviewItemStepId);
        Assert.Equal("Reject", result.Decision);
    }

    [Fact]
    public async Task GetRecommendationSummaryAsync_WithRecommendations_ReturnsCorrectAggregation()
    {
        // Arrange
        var certificationProcessId = 300L;
        await SeedRecommendation(certificationProcessId, 1, "Approve", 0.9m, "Low");
        await SeedRecommendation(certificationProcessId, 2, "Approve", 0.85m, "Low");
        await SeedRecommendation(certificationProcessId, 3, "Reject", 0.88m, "High", hasSodViolation: true);
        await SeedRecommendation(certificationProcessId, 4, "NeedsReview", 0.55m, "Medium");
        await SeedRecommendation(certificationProcessId, 5, "Reject", 0.92m, "Critical", isAnomaly: true);

        // Act
        var summary = await _service.GetRecommendationSummaryAsync(certificationProcessId);

        // Assert
        Assert.Equal(5, summary.TotalItems);
        Assert.Equal(2, summary.RecommendedApprove);
        Assert.Equal(2, summary.RecommendedReject);
        Assert.Equal(1, summary.NeedsReview);
        Assert.Equal(2, summary.HighRiskCount); // High + Critical
        Assert.Equal(1, summary.AnomalyCount);
        Assert.Equal(1, summary.SodViolationCount);
        Assert.Equal("Generated", summary.Status);
    }

    [Fact]
    public async Task GetRecommendationSummaryAsync_WithNoRecommendations_ReturnsNotGeneratedStatus()
    {
        // Arrange
        var certificationProcessId = 400L;

        // Act
        var summary = await _service.GetRecommendationSummaryAsync(certificationProcessId);

        // Assert
        Assert.Equal(0, summary.TotalItems);
        Assert.Equal("NotGenerated", summary.Status);
    }

    [Fact]
    public async Task RecordFeedbackAsync_CreatesNewFeedback()
    {
        // Arrange
        var certificationProcessId = 500L;
        var stepId = 1L;
        var recSysId = Guid.NewGuid();

        var recommendation = new AIRecommendation
        {
            SysId = recSysId,
            CertificationProcessId = certificationProcessId,
            ReviewItemStepId = stepId,
            Decision = "Approve",
            ConfidenceScore = 0.9m,
            RiskLevel = "Low",
            Status = "Generated",
            HasSodViolation = false,
            IsAnomaly = false,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.Set<AIRecommendation>().AddAsync(recommendation);
        await _dbContext.SaveChangesAsync();

        var reviewerUserId = Guid.NewGuid();
        var feedbackRequest = new AIRecommendationFeedbackRequest
        {
            ActualDecision = "Reject",
            AgreedWithAI = false,
            OverrideReason = "Business requirement changed",
            FeedbackComments = "User needs this role temporarily",
            QualityRating = 3
        };

        // Act
        await _service.RecordFeedbackAsync(recSysId, reviewerUserId, feedbackRequest);

        // Assert
        var feedback = await _dbContext.Set<AIRecommendationFeedback>()
            .FirstOrDefaultAsync(f => f.AIRecommendationId == recSysId);

        Assert.NotNull(feedback);
        Assert.Equal("Reject", feedback.ActualDecision);
        Assert.False(feedback.AgreedWithAI);
        Assert.Equal("Business requirement changed", feedback.OverrideReason);
        Assert.Equal(reviewerUserId, feedback.ReviewerUserId);
    }

    [Fact]
    public async Task RecordFeedbackAsync_NonexistentRecommendation_Throws()
    {
        // Arrange
        var feedbackRequest = new AIRecommendationFeedbackRequest
        {
            ActualDecision = "Reject",
            AgreedWithAI = false
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RecordFeedbackAsync(Guid.NewGuid(), Guid.NewGuid(), feedbackRequest));
    }

    [Fact]
    public async Task HasRecommendationsAsync_WithRecommendations_ReturnsTrue()
    {
        // Arrange
        var certificationProcessId = 600L;
        await SeedRecommendation(certificationProcessId, 1, "Approve", 0.9m, "Low");

        // Act
        var exists = await _service.HasRecommendationsAsync(certificationProcessId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task HasRecommendationsAsync_WithNoRecommendations_ReturnsFalse()
    {
        // Act
        var exists = await _service.HasRecommendationsAsync(999);

        // Assert
        Assert.False(exists);
    }

    private async Task SeedRecommendation(
        long certificationProcessId,
        long stepId,
        string decision,
        decimal confidence,
        string riskLevel,
        bool hasSodViolation = false,
        bool isAnomaly = false)
    {
        var rec = new AIRecommendation
        {
            SysId = Guid.NewGuid(),
            CertificationProcessId = certificationProcessId,
            ReviewItemStepId = stepId,
            Decision = decision,
            ConfidenceScore = confidence,
            RiskLevel = riskLevel,
            RiskSummary = $"{decision} recommendation",
            HasSodViolation = hasSodViolation,
            IsAnomaly = isAnomaly,
            AnomalyScore = isAnomaly ? 0.8m : null,
            Status = "Generated",
            GeneratedAt = DateTimeOffset.UtcNow,
        };

        await _dbContext.Set<AIRecommendation>().AddAsync(rec);
        await _dbContext.SaveChangesAsync();
    }
}
