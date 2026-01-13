using MediatR;

namespace LightRAGNet.Models;

/// <summary>
/// Task status change event
/// </summary>
public record RagTaskStatusChangedEvent(RagTask Task) : INotification;
