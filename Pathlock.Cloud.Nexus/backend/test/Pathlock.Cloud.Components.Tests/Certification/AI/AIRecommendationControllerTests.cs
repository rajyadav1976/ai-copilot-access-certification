using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Pathlock.Cloud.Components.Certification;
using Pathlock.Cloud.Components.Certification.AI;
using Pathlock.Cloud.Components.Certification.AI.Agent;
using Pathlock.Cloud.Components.Certification.AI.Models;
using Pathlock.Cloud.Platform.Session;
using Xunit;

namespace Pathlock.Cloud.Components.Tests.Certification.AI;

public class AIRecommendationControllerTests
{
    private readonly Mock<IAIRecommendationService> _aiServiceMock;
    private readonly Mock<IRecommendationEngine> _engineMock;
    private readonly Mock<IReviewerAssistant> _reviewerAssistantMock;
    private readonly Mock<IFeedbackAnalyticsService> _feedbackAnalyticsMock;
    private readonly Mock<ISessionContext> _sessionContextMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IAIGenerationTracker> _trackerMock;
    private readonly Mock<ILogger<AIRecommendationController>> _loggerMock;
    private readonly AIRecommendationController _controller;
    private readonly Guid _principalId = Guid.NewGuid();

    public AIRecommendationControllerTests()
    {
        _aiServiceMock = new Mock<IAIRecommendationService>();
        _engineMock = new Mock<IRecommendationEngine>();
        _reviewerAssistantMock = new Mock<IReviewerAssistant>();
        _feedbackAnalyticsMock = new Mock<IFeedbackAnalyticsService>();
        _sessionContextMock = new Mock<ISessionContext>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _trackerMock = new Mock<IAIGenerationTracker>();
        _loggerMock = new Mock<ILogger<AIRecommendationController>>();

        // Setup PrincipalContext for authenticated user
        var principalContextMock = new Mock<IPrincipalContext>();
        principalContextMock.Setup(p => p.Id).Returns(_principalId);
        principalContextMock.Setup(p => p.DisplayName).Returns("testuser");
        _sessionContextMock.Setup(s => s.PrincipalContext).Returns(principalContextMock.Object);

        _controller = new AIRecommendationController(
            _aiServiceMock.Object,
            _engineMock.Object,
            _reviewerAssistantMock.Object,
            _feedbackAnalyticsMock.Object,
            _sessionContextMock.Object,
            _scopeFactoryMock.Object,
            _trackerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetRecommendations_ReturnsOkWithList()
    {
        // Arrange
        var recs = new List<AIRecommendationDto>
        {
            new() { Id = Guid.NewGuid(), Decision = "Approve", ConfidenceScore = 0.9m, RiskLevel = "Low", ReviewItemStepId = 1 }
        };
        _aiServiceMock
            .Setup(s => s.GetRecommendationsAsync(100L, default))
            .ReturnsAsync(recs);

        // Act
        var result = await _controller.GetRecommendations(100L);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedRecs = Assert.IsAssignableFrom<IReadOnlyList<AIRecommendationDto>>(okResult.Value);
        Assert.Single(returnedRecs);
    }

    [Fact]
    public async Task GetSummary_ReturnsOkWithSummary()
    {
        // Arrange
        var summary = new AIRecommendationSummary
        {
            TotalItems = 10,
            RecommendedApprove = 5,
            RecommendedReject = 3,
            NeedsReview = 2,
            HighRiskCount = 3,
            AnomalyCount = 1,
            SodViolationCount = 2,
            AverageConfidence = 0.82m,
            Status = "Generated"
        };
        _aiServiceMock
            .Setup(s => s.GetRecommendationSummaryAsync(200L, default))
            .ReturnsAsync(summary);

        // Act
        var result = await _controller.GetSummary(200L);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedSummary = Assert.IsType<AIRecommendationSummary>(okResult.Value);
        Assert.Equal(10, returnedSummary.TotalItems);
        Assert.Equal("Generated", returnedSummary.Status);
    }

    [Fact]
    public async Task GenerateRecommendations_WhenAlreadyGenerated_ReturnsOkWithExistingSummary()
    {
        // Arrange — summary already generated
        var existingSummary = new AIRecommendationSummary
        {
            CertificationProcessId = 300,
            TotalItems = 5,
            RecommendedApprove = 3,
            RecommendedReject = 1,
            NeedsReview = 1,
            Status = "Generated"
        };

        _aiServiceMock
            .Setup(s => s.GetRecommendationSummaryAsync(300L, default))
            .ReturnsAsync(existingSummary);

        // Act
        var result = await _controller.GenerateRecommendations(300L, null);

        // Assert — returns existing without triggering engine
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summary = Assert.IsType<AIRecommendationSummary>(okResult.Value);
        Assert.Equal(5, summary.TotalItems);
        Assert.Equal("Generated", summary.Status);
        _engineMock.Verify(
            e => e.GenerateRecommendationsAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<long[]?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateRecommendations_WhenNotGenerated_ReturnsAcceptedProcessing()
    {
        // Arrange — no existing recommendations
        var notGeneratedSummary = new AIRecommendationSummary
        {
            CertificationProcessId = 300,
            TotalItems = 0,
            Status = "NotGenerated"
        };

        // Setup scope factory for background task
        var scopeMock = new Mock<IServiceScope>();
        var scopedProviderMock = new Mock<IServiceProvider>();
        var scopedAccessorMock = new Mock<ISessionContextAccessor>();
        var scopedEngineMock = new Mock<IRecommendationEngine>();
        scopedEngineMock
            .Setup(e => e.GenerateRecommendationsAsync(300L, "testuser", false, null, default))
            .ReturnsAsync(new AIRecommendationSummary { Status = "Generated" });

        scopedProviderMock.Setup(p => p.GetService(typeof(ISessionContextAccessor))).Returns(scopedAccessorMock.Object);
        scopedProviderMock.Setup(p => p.GetService(typeof(IRecommendationEngine))).Returns(scopedEngineMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(scopedProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        _aiServiceMock
            .Setup(s => s.GetRecommendationSummaryAsync(300L, default))
            .ReturnsAsync(notGeneratedSummary);

        // Act
        var result = await _controller.GenerateRecommendations(300L, null);

        // Assert — returns 202 Accepted with Processing status
        var acceptedResult = Assert.IsType<AcceptedResult>(result);
        var summary = Assert.IsType<AIRecommendationSummary>(acceptedResult.Value);
        Assert.Equal("Processing", summary.Status);
    }

    [Fact]
    public async Task HasRecommendations_WithExisting_ReturnsOk()
    {
        // Arrange
        _aiServiceMock
            .Setup(s => s.HasRecommendationsAsync(400L, default))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.HasRecommendations(400L);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task HasRecommendations_WithNone_ReturnsOk()
    {
        // Arrange
        _aiServiceMock
            .Setup(s => s.HasRecommendationsAsync(500L, default))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.HasRecommendations(500L);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task RecordFeedback_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var recId = Guid.NewGuid();
        var feedbackRequest = new AIRecommendationFeedbackRequest
        {
            ActualDecision = "Approve",
            AgreedWithAI = true,
            QualityRating = 5
        };

        _aiServiceMock
            .Setup(s => s.RecordFeedbackAsync(recId, _principalId, feedbackRequest, default))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RecordFeedback(recId, feedbackRequest);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task RecordFeedback_WithMissingDecision_ReturnsBadRequest()
    {
        // Arrange
        var feedbackRequest = new AIRecommendationFeedbackRequest
        {
            ActualDecision = "",  // Missing
            AgreedWithAI = true
        };

        // Act
        var result = await _controller.RecordFeedback(Guid.NewGuid(), feedbackRequest);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RecordFeedback_WithNoPrincipalContext_ReturnsUnauthorized()
    {
        // Arrange - no principal context
        _sessionContextMock.Setup(s => s.PrincipalContext).Returns((IPrincipalContext?)null);
        var controller = new AIRecommendationController(
            _aiServiceMock.Object,
            _engineMock.Object,
            _reviewerAssistantMock.Object,
            _feedbackAnalyticsMock.Object,
            _sessionContextMock.Object,
            _scopeFactoryMock.Object,
            _trackerMock.Object,
            _loggerMock.Object);

        var feedbackRequest = new AIRecommendationFeedbackRequest
        {
            ActualDecision = "Reject",
            AgreedWithAI = false
        };

        // Act
        var result = await controller.RecordFeedback(Guid.NewGuid(), feedbackRequest);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}
