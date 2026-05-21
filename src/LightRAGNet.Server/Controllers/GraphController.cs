using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.GraphCuration;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/graph")]
public sealed class GraphController(
    IGraphStore graphStore,
    GraphCurationService graphCurationService,
    ILogger<GraphController> logger) : ControllerBase
{
    [HttpGet("entity/exists")]
    [ProducesResponseType<GraphEntityExistsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GraphEntityExistsResponse>> EntityExists(
        [FromQuery] string? name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(CreateValidationResponse("Entity name is required."));
        }

        var exists = await graphCurationService.EntityExistsAsync(name, cancellationToken);
        return Ok(new GraphEntityExistsResponse(exists));
    }

    [HttpPost("entity")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> CreateEntity(
        [FromBody] GraphEntityCreateDto? request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateEntityCreate(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var result = await graphCurationService.CreateEntityAsync(
            new GraphEntityCreateRequest(request!.EntityName!, request.EntityData!),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPatch("entity/{name}")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> EditEntity(
        string? name,
        [FromBody] GraphEntityEditDto? request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateEntityEdit(name, request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var result = await graphCurationService.EditEntityAsync(
            new GraphEntityEditRequest(
                name!,
                request!.UpdatedData!,
                request.AllowRename,
                request.AllowMerge),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("relation")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> CreateRelation(
        [FromBody] GraphRelationCreateDto? request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRelationCreate(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var result = await graphCurationService.CreateRelationAsync(
            new GraphRelationCreateRequest(
                request!.SourceEntity!,
                request.TargetEntity!,
                request.RelationData!),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPatch("relation")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> EditRelation(
        [FromBody] GraphRelationEditDto? request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRelationEdit(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var result = await graphCurationService.EditRelationAsync(
            new GraphRelationEditRequest(
                request!.SourceEntity!,
                request.TargetEntity!,
                request.UpdatedData!),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("entities/merge")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> MergeEntities(
        [FromBody] GraphEntityMergeDto? request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateEntityMerge(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var result = await graphCurationService.MergeEntitiesAsync(
            new GraphEntityMergeRequest(request!.SourceEntities!, request.TargetEntity!),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("entity/{name}")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> DeleteEntity(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(CreateValidationResponse("Entity name is required."));
        }

        var result = await graphCurationService.DeleteEntityAsync(name, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("relation")]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<GraphCurationResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphCurationResponse>> DeleteRelation(
        [FromQuery] string? source,
        [FromQuery] string? target,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            return BadRequest(CreateValidationResponse("Relation source and target are required."));
        }

        var result = await graphCurationService.DeleteRelationAsync(source, target, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("labels")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetLabels(CancellationToken cancellationToken = default)
    {
        try
        {
            var labels = await graphStore.GetPopularLabelsAsync(300, cancellationToken);
            return Ok(labels);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch graph labels.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private ActionResult<GraphCurationResponse> ToActionResult(GraphCurationOperationResult result)
    {
        var response = ToResponse(result);
        if (result.Succeeded)
        {
            return Ok(response);
        }

        return StatusCode(ToHttpStatusCode(result.Status), response);
    }

    private static int ToHttpStatusCode(string status)
    {
        return status switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "validation_error" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static GraphCurationResponse ToResponse(GraphCurationOperationResult result)
    {
        return new GraphCurationResponse(
            result.Succeeded,
            result.Status,
            result.Message,
            result.Data,
            ToSummaryDto(result.OperationSummary),
            result.FailureStage);
    }

    private static GraphCurationSummaryDto? ToSummaryDto(GraphCurationOperationSummary? summary)
    {
        return summary is null
            ? null
            : new GraphCurationSummaryDto(
                summary.Merged,
                summary.MergeStatus,
                summary.MergeError,
                summary.OperationStatus,
                summary.TargetEntity,
                summary.FinalEntity,
                summary.Renamed);
    }

    private static GraphCurationResponse CreateValidationResponse(string message)
    {
        return new GraphCurationResponse(
            Succeeded: false,
            Status: "validation_error",
            Message: message,
            Data: null,
            OperationSummary: null,
            FailureStage: "validation");
    }

    private static GraphCurationResponse? ValidateEntityCreate(GraphEntityCreateDto? request)
    {
        if (request is null)
        {
            return CreateValidationResponse("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return CreateValidationResponse("Entity name is required.");
        }

        return request.EntityData is null
            ? CreateValidationResponse("Entity data is required.")
            : null;
    }

    private static GraphCurationResponse? ValidateEntityEdit(string? name, GraphEntityEditDto? request)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateValidationResponse("Entity name is required.");
        }

        if (request is null)
        {
            return CreateValidationResponse("Request body is required.");
        }

        return request.UpdatedData is null
            ? CreateValidationResponse("Updated entity data is required.")
            : null;
    }

    private static GraphCurationResponse? ValidateRelationCreate(GraphRelationCreateDto? request)
    {
        if (request is null)
        {
            return CreateValidationResponse("Request body is required.");
        }

        var endpointValidation = ValidateRelationEndpoints(request.SourceEntity, request.TargetEntity);
        if (endpointValidation is not null)
        {
            return endpointValidation;
        }

        return request.RelationData is null
            ? CreateValidationResponse("Relation data is required.")
            : null;
    }

    private static GraphCurationResponse? ValidateRelationEdit(GraphRelationEditDto? request)
    {
        if (request is null)
        {
            return CreateValidationResponse("Request body is required.");
        }

        var endpointValidation = ValidateRelationEndpoints(request.SourceEntity, request.TargetEntity);
        if (endpointValidation is not null)
        {
            return endpointValidation;
        }

        return request.UpdatedData is null
            ? CreateValidationResponse("Updated relation data is required.")
            : null;
    }

    private static GraphCurationResponse? ValidateEntityMerge(GraphEntityMergeDto? request)
    {
        if (request is null)
        {
            return CreateValidationResponse("Request body is required.");
        }

        if (request.SourceEntities is null || request.SourceEntities.Count == 0)
        {
            return CreateValidationResponse("Source entities are required.");
        }

        if (request.SourceEntities.Any(string.IsNullOrWhiteSpace))
        {
            return CreateValidationResponse("Source entity names are required.");
        }

        return string.IsNullOrWhiteSpace(request.TargetEntity)
            ? CreateValidationResponse("Target entity is required.")
            : null;
    }

    private static GraphCurationResponse? ValidateRelationEndpoints(string? source, string? target)
    {
        return string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)
            ? CreateValidationResponse("Relation source and target are required.")
            : null;
    }
}
