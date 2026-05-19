using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class RagTaskNotificationServiceSourceTests
{
    [Fact]
    public void RagTaskNotificationService_TaskStatusUpdates_AreQueuedThroughDispatcher()
    {
        var source = ReadServiceSource();

        source.Should().NotContain("_ = NotifyTaskStatusHandlersAsync(update);");
        source.Should().Contain("EnqueueLatestAsync(update");
    }

    [Fact]
    public void RagTaskNotificationService_DataClearedNotifications_AreQueuedAfterStatusDrain()
    {
        var source = ReadServiceSource();

        source.Should().NotContain("_ = NotifyDataClearedHandlersAsync();");
        source.Should().Contain("EnqueueAsync(NotificationDispatchKey.DataCleared");
        source.Should().Contain("DrainAsync(token)");
    }

    [Fact]
    public void RagTaskNotificationService_HandlerFailures_AreIsolatedPerSubscriber()
    {
        var source = NormalizeLineEndings(ReadServiceSource());

        source.Should().Contain(
            "cancellationToken.ThrowIfCancellationRequested();\n" +
            "            try\n" +
            "            {\n" +
            "                await handler(this, update);\n" +
            "            }\n" +
            "            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)");
        source.Should().Contain(
            "cancellationToken.ThrowIfCancellationRequested();\n" +
            "            try\n" +
            "            {\n" +
            "                await handler(this, EventArgs.Empty);\n" +
            "            }\n" +
            "            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)");
        source.Should().Contain("cancellationToken.IsCancellationRequested");
        source.Should().Contain("catch (Exception ex)");
    }

    [Fact]
    public void RagTaskNotificationService_ConnectionStateChanges_AreDispatchedThroughIsolatedHelper()
    {
        var source = NormalizeLineEndings(ReadServiceSource());

        source.Should().Contain("NotifyConnectionStateChangedHandlersAsync(string state, CancellationToken cancellationToken = default)");
        source.Should().NotContain("Task.WhenAll(tasks)");
        source.Should().NotContain("ConnectionStateChanged.GetInvocationList()");
        CountOccurrences(source, "ConnectionStateChanged?.GetInvocationList()").Should().Be(1);
        source.Should().Contain(
            "cancellationToken.ThrowIfCancellationRequested();\n" +
            "            try\n" +
            "            {\n" +
            "                await handler(this, state);\n" +
            "            }\n" +
            "            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)");
        source.Should().Contain("NotifyConnectionStateChangedHandlersAsync(\"Connected\", disposeToken)");
        source.Should().Contain("NotifyConnectionStateChangedHandlersAsync(\"ServerNotStarted\", disposeToken)");
        source.Should().Contain("NotifyConnectionStateChangedHandlersAsync(\"Disconnected\", disposeToken)");
    }

    [Fact]
    public void RagTaskNotificationService_HubLifecycleCallbacks_DoNotLeakDisposeCancellation()
    {
        var source = NormalizeLineEndings(ReadServiceSource());

        source.Should().Contain("NotifyConnectionStateChangedFromHubCallbackAsync(string state, CancellationToken cancellationToken)");
        source.Should().Contain(
            "catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n" +
            "        {\n" +
            "            logger.LogDebug(\"SignalR lifecycle notification skipped because service is disposing: State={State}\", state);");
        source.Should().Contain("NotifyConnectionStateChangedFromHubCallbackAsync(\"Disconnected\", disposeToken)");
        source.Should().Contain("NotifyConnectionStateChangedFromHubCallbackAsync(\"Reconnecting\", disposeToken)");
        source.Should().Contain("NotifyConnectionStateChangedFromHubCallbackAsync(\"Connected\", disposeToken)");
    }

    [Fact]
    public void RagTaskNotificationService_ReconnectedCallback_RejoinsTaskGroupWithDisposeToken()
    {
        var source = NormalizeLineEndings(ReadServiceSource());

        source.Should().Contain(
            "_hubConnection.Reconnected += async (connectionId) =>\n" +
            "            {\n" +
            "                logger.LogInformation(\"SignalR reconnected: ConnectionId={ConnectionId}\", connectionId);\n" +
            "                await NotifyConnectionStateChangedFromHubCallbackAsync(\"Connected\", disposeToken);\n" +
            "                \n" +
            "                // Rejoin all task groups after reconnection\n" +
            "                await JoinAllTasksGroupAsync(disposeToken);");
    }

    [Fact]
    public void RagTaskNotificationService_Dispose_CoordinatesWithInitialization()
    {
        var source = ReadServiceSource();
        var initializeStart = source.IndexOf("public async Task InitializeAsync()", StringComparison.Ordinal);
        var initializeDisposeGateIndex = source.IndexOf("lock (_disposeGate)", initializeStart, StringComparison.Ordinal);
        var initializeDisposedGuardIndex = source.IndexOf("if (_disposed)", initializeStart, StringComparison.Ordinal);
        var initializeDisposeTokenIndex = source.IndexOf("disposeToken = _disposeCts.Token;", initializeStart, StringComparison.Ordinal);

        source.Should().Contain("CancellationTokenSource _disposeCts");
        source.Should().Contain("bool _disposed");
        source.Should().Contain("var lockTaken = false;");
        source.Should().Contain("WaitAsync(disposeToken)");
        source.Should().Contain("Task.Delay(100, disposeToken)");
        source.Should().Contain("StartAsync(disposeToken)");
        source.Should().Contain("if (lockTaken)");
        initializeStart.Should().BeGreaterThanOrEqualTo(0);
        initializeDisposeGateIndex.Should().BeGreaterThan(initializeStart);
        initializeDisposedGuardIndex.Should().BeGreaterThan(initializeDisposeGateIndex);
        initializeDisposeTokenIndex.Should().BeGreaterThan(initializeDisposedGuardIndex);
    }

    [Fact]
    public void RagTaskNotificationService_DataClearedDrain_DocumentsAcceptedEventSnapshotSemantics()
    {
        var source = ReadServiceSource();

        source.Should().Contain("DrainAsync(token)");
        source.Should().Contain("accepted task-status updates");
        source.Should().Contain("snapshot");
        source.Should().NotContain("global barrier");
    }

    private static string ReadServiceSource()
    {
        var repositoryRoot = FindRepositoryRoot();
        var servicePath = Path.Combine(
            repositoryRoot,
            "src",
            "LightRAGNet.Web",
            "Services",
            "RagTaskNotificationService.cs");

        return File.ReadAllText(servicePath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightRAGNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing LightRAGNet.slnx.");
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
}
