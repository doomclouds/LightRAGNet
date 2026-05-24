import { HubConnectionBuilder } from '@microsoft/signalr';
import { buildUrl } from './http';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';

export type RagTaskHubConnectionState =
  | 'Connecting'
  | 'Connected'
  | 'Disconnected'
  | 'Reconnecting'
  | 'ServerNotStarted';

export type RagTaskHubHandlers = {
  onTaskStatusUpdated?: (update: TaskStatusUpdate) => void;
  onDataCleared?: () => void;
  onConnectionStateChanged?: (state: RagTaskHubConnectionState) => void;
};

export type RagTaskHubConnection = {
  on(eventName: 'TaskStatusUpdated', callback: (update: TaskStatusUpdate) => void): void;
  on(eventName: 'DataCleared', callback: () => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
  invoke(methodName: 'JoinAllTasksGroup' | 'LeaveAllTasksGroup'): Promise<void>;
  start(): Promise<void>;
  stop(): Promise<void>;
};

export type RagTaskHubConnectionFactory = (hubUrl: string) => RagTaskHubConnection;

export type RagTaskHubClient = {
  configure(handlers: RagTaskHubHandlers): void;
  start(handlers?: RagTaskHubHandlers): Promise<void>;
  stop(): Promise<void>;
};

const createDefaultConnection: RagTaskHubConnectionFactory = (hubUrl) =>
  new HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .build();

export function createRagTaskHubClient(
  apiBase: string,
  factory: RagTaskHubConnectionFactory = createDefaultConnection
): RagTaskHubClient {
  const connection = factory(buildUrl(apiBase, '/hubs/ragtask'));
  let currentHandlers: RagTaskHubHandlers = {};
  let connectionCallbacksConfigured = false;
  let taskEventDispatchersConfigured = false;
  let isStartedAndJoined = false;
  let startPromise: Promise<void> | undefined;
  let lifecycleGeneration = 0;

  function emitConnectionState(state: RagTaskHubConnectionState): void {
    currentHandlers.onConnectionStateChanged?.(state);
  }

  function configureConnectionCallbacks(): void {
    if (connectionCallbacksConfigured) {
      return;
    }

    connection.onreconnecting(() => {
      isStartedAndJoined = false;
      emitConnectionState('Reconnecting');
    });
    connection.onreconnected(() => {
      void joinAllTasksGroup()
        .then(() => {
          isStartedAndJoined = true;
          emitConnectionState('Connected');
        })
        .catch(() => {
          isStartedAndJoined = false;
          emitConnectionState('Disconnected');
        });
    });
    connection.onclose(() => {
      isStartedAndJoined = false;
      emitConnectionState('Disconnected');
    });
    connectionCallbacksConfigured = true;
  }

  function configureTaskEventDispatchers(): void {
    if (taskEventDispatchersConfigured) {
      return;
    }

    connection.on('TaskStatusUpdated', (update) => currentHandlers.onTaskStatusUpdated?.(update));
    connection.on('DataCleared', () => currentHandlers.onDataCleared?.());
    taskEventDispatchersConfigured = true;
  }

  async function joinAllTasksGroup(): Promise<void> {
    await connection.invoke('JoinAllTasksGroup');
  }

  async function leaveAllTasksGroup(): Promise<void> {
    try {
      await connection.invoke('LeaveAllTasksGroup');
    } catch {
      // Best effort only: stopping the SignalR connection still matters after leave failures.
    }
  }

  function configure(handlers: RagTaskHubHandlers): void {
    currentHandlers = { ...currentHandlers, ...handlers };
    configureConnectionCallbacks();
    configureTaskEventDispatchers();
  }

  async function cleanupHalfOpenConnection(): Promise<void> {
    try {
      await connection.stop();
    } catch {
      // Best effort cleanup after a half-open or aborted start.
    }
  }

  async function startConnection(generation: number): Promise<void> {
    try {
      await connection.start();
    } catch {
      if (generation !== lifecycleGeneration) {
        isStartedAndJoined = false;
        return;
      }

      emitConnectionState('ServerNotStarted');
      return;
    }

    if (generation !== lifecycleGeneration) {
      await cleanupHalfOpenConnection();
      isStartedAndJoined = false;
      return;
    }

    try {
      await joinAllTasksGroup();
      isStartedAndJoined = true;
      emitConnectionState('Connected');
    } catch (error) {
      await cleanupHalfOpenConnection();
      isStartedAndJoined = false;
      emitConnectionState('Disconnected');
    }
  }

  return {
    configure,
    async start(handlers?: RagTaskHubHandlers): Promise<void> {
      if (handlers) {
        configure(handlers);
      } else {
        configureConnectionCallbacks();
      }

      if (isStartedAndJoined) {
        return;
      }

      if (!startPromise) {
        const generation = lifecycleGeneration;
        startPromise = startConnection(generation).finally(() => {
          startPromise = undefined;
        });
      }

      await startPromise;
    },
    async stop(): Promise<void> {
      lifecycleGeneration += 1;
      await leaveAllTasksGroup();
      await connection.stop();
      isStartedAndJoined = false;
      emitConnectionState('Disconnected');
    }
  };
}
