using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

/// <summary>
/// Graph view controller for knowledge graph visualization
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GraphViewController(
    IGraphStore graphStore,
    ILogger<GraphViewController> logger) : ControllerBase
{
    /// <summary>
    /// Get knowledge graph data for visualization
    /// </summary>
    /// <param name="nodeLabel">Node label filter (use "*" for all nodes)</param>
    /// <param name="maxDepth">Maximum depth for graph traversal (default: 2)</param>
    /// <param name="maxNodes">Maximum number of nodes to return (default: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Graph view data</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GraphViewDto>> GetGraphView(
        [FromQuery] string? nodeLabel = "*",
        [FromQuery] int maxDepth = 2,
        [FromQuery] int maxNodes = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (maxNodes <= 0 || maxNodes > 1000)
            {
                return BadRequest(new { error = "maxNodes must be between 1 and 1000" });
            }

            if (maxDepth <= 0 || maxDepth > 5)
            {
                return BadRequest(new { error = "maxDepth must be between 1 and 5" });
            }

            // Get knowledge graph from Neo4j
            var knowledgeGraph = await graphStore.GetKnowledgeGraphAsync(
                nodeLabel ?? "*",
                maxDepth,
                maxNodes,
                cancellationToken);

            // Convert to DTO for sigma.js
            var dto = ConvertToGraphViewDto(knowledgeGraph);

            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching graph view data");
            return StatusCode(500, new { error = "Failed to fetch graph data", message = ex.Message });
        }
    }

    /// <summary>
    /// Convert KnowledgeGraph to GraphViewDto for sigma.js
    /// </summary>
    private static GraphViewDto ConvertToGraphViewDto(KnowledgeGraph graph)
    {
        // Color mapping for different entity types
        var typeColors = new Dictionary<string, string>
        {
            { "Person", "#FF6B6B" },
            { "Organization", "#4ECDC4" },
            { "Location", "#95E1D3" },
            { "Concept", "#F38181" },
            { "Event", "#AA96DA" },
            { "Method", "#FCBAD3" },
            { "Content", "#A8DADC" },
            { "Data", "#FFD93D" },
            { "Artifact", "#6BCB77" },
            { "NaturalObject", "#4D96FF" },
            { "Creature", "#9B59B6" }
        };

        var defaultColor = "#666666"; // Darker gray for better visibility

        // Convert nodes
        var nodes = graph.Nodes.Select((node, index) =>
        {
            var entityType = node.Properties.TryGetValue("entity_type", out var type) 
                ? type?.ToString() 
                : null;
            
            var label = node.Properties.TryGetValue("entity_id", out var id) 
                ? id?.ToString() ?? node.Id 
                : node.Id;

            // Truncate long labels
            if (label.Length > 20)
            {
                label = label[..17] + "...";
            }

            // Node size will be calculated on frontend based on actual edge connections
            // We don't calculate it here as we need to count edges first
            var size = 8.0; // Default size, will be adjusted by frontend

            // Get color based on entity type
            // If entity_type is not found or not in mapping, use hash-based coloring for variety
            string color = defaultColor;
            if (entityType != null && typeColors.TryGetValue(entityType, out var c))
            {
                color = c;
            }
            else
            {
                // Use a color based on hash of label for consistent coloring
                // This ensures different nodes get different colors even without entity_type
                var hash = Math.Abs(label.GetHashCode());
                var colorIndex = hash % typeColors.Count;
                color = typeColors.Values.ElementAt(colorIndex);
            }

            return new GraphNodeDto
            {
                Id = node.Id,
                Label = label,
                Size = size,
                Color = color,
                Type = entityType,
                Properties = node.Properties
            };
        }).ToList();

        // Convert edges
        var edges = graph.Edges.Select((edge, index) => new GraphEdgeDto
        {
            Id = edge.Id ?? $"edge_{index}",
            Source = edge.Source,
            Target = edge.Target,
            Type = edge.Type ?? "DIRECTED",
            Size = 1.0,
            Color = "#cccccc"
        }).ToList();

        return new GraphViewDto
        {
            Nodes = nodes,
            Edges = edges,
            IsTruncated = graph.IsTruncated
        };
    }
}
