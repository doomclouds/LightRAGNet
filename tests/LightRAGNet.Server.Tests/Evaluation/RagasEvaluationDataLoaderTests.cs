using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationDataLoaderTests
{
    [Fact]
    public async Task LoadCasesAsync_DefaultRequest_ReturnsDatasetCases()
    {
        var loader = CreateLoader(maxCasesPerRun: 5);

        var result = await loader.LoadCasesAsync([], maxCases: null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
        result.Value![0].CaseName.Should().Be(
            "case-1-how-does-lightrag-solve-the-hallucination-problem-in-large-language-models");
        result.Value[0].Question.Should().Be(
            "How does LightRAG solve the hallucination problem in large language models?");
        result.Value[0].GroundTruth.Should().NotBeNullOrWhiteSpace();
        result.Value[0].Project.Should().Be("lightrag_evaluation_sample");
    }

    [Fact]
    public async Task LoadCasesAsync_WhenCaseNamesIsNull_ReturnsDatasetCases()
    {
        var loader = CreateLoader(maxCasesPerRun: 5);

        var result = await loader.LoadCasesAsync(null!, maxCases: null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoadCasesAsync_WhenCaseNamesProvided_ReturnsExactRequestedCases()
    {
        var loader = CreateLoader(maxCasesPerRun: 10);
        var requestedNames = new[]
        {
            "case-2-what-are-the-three-main-components-required-in-a-rag-system",
            "case-4-what-vector-databases-does-lightrag-support-and-what-are-their-key-characteristics"
        };

        var result = await loader.LoadCasesAsync(requestedNames, maxCases: null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Select(static item => item.CaseName).Should().Equal(requestedNames);
    }

    [Fact]
    public async Task LoadCasesAsync_WhenCaseNameIsUnknown_ReturnsUnknownCaseFailure()
    {
        var loader = CreateLoader(maxCasesPerRun: 10);

        var result = await loader.LoadCasesAsync(["missing-case"], maxCases: null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("unknown_case");
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task LoadCasesAsync_WhenMaxCasesExceedsConfiguredLimit_ReturnsMaxCasesExceededFailure()
    {
        var loader = CreateLoader(maxCasesPerRun: 2);

        var result = await loader.LoadCasesAsync([], maxCases: 3, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("max_cases_exceeded");
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task LoadCasesAsync_WhenMaxCasesIsPositive_ReturnsRequestedCount()
    {
        var loader = CreateLoader(maxCasesPerRun: 10);

        var result = await loader.LoadCasesAsync([], maxCases: 2, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task LoadCasesAsync_WhenMaxCasesIsNotPositive_ReturnsInvalidMaxCasesFailure(int maxCases)
    {
        var loader = CreateLoader(maxCasesPerRun: 10);

        var result = await loader.LoadCasesAsync([], maxCases, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_max_cases");
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private static RagasEvaluationDataLoader CreateLoader(int maxCasesPerRun) =>
        new(Options.Create(new RagasEvaluationOptions { MaxCasesPerRun = maxCasesPerRun }));
}
