export function notifySubscribers<TArgs extends unknown[]>(
  subscribers: Iterable<(...args: TArgs) => void>,
  ...args: TArgs
): void {
  for (const subscriber of subscribers) {
    try {
      subscriber(...args);
    } catch (error) {
      // Subscriber failures should not break SignalR fan-out to later listeners.
      console.error('LightRAGNet subscriber notification failed.', error);
    }
  }
}
