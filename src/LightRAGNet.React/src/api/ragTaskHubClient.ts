import { HubConnectionBuilder } from '@microsoft/signalr';
import { buildUrl } from './http';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';

export type RagTaskHubConnectionState = 'Connected' | 'Disconnected' | 'Reconnecting' | 'ServerNotStarted';

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

  function emitConnectionState(state: RagTaskHubConnectionState): void {
    currentHandlers.onConnectionStateChanged?.(state);
  }

  function configureConnectionCallbacks(): void {
    if (connectionCallbacksConfigured) {
      return;
    }

    connection.onreconnecting(() => emitConnectionState('Reconnecting'));
    connection.onreconnected(() => emitConnectionState('Connected'));
    connection.onclose(() => emitConnectionState('Disconnected'));
    connectionCallbacksConfigured = true;
  }

  function configure(handlers: RagTaskHubHandlers): void {
    currentHandlers = { ...currentHandlers, ...handlers };
    configureConnectionCallbacks();

    if (handlers.onTaskStatusUpdated) {
      connection.on('TaskStatusUpdated', handlers.onTaskStatusUpdated);
    }

    if (handlers.onDataCleared) {
      connection.on('DataCleared', handlers.onDataCleared);
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

      try {
        await connection.start();
        emitConnectionState('Connected');
      } catch {
        emitConnectionState('ServerNotStarted');
      }
    },
    async stop(): Promise<void> {
      await connection.stop();
      emitConnectionState('Disconnected');
    }
  };
}
