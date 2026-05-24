import { act, fireEvent, render, screen } from '@testing-library/react';
import type { ComponentType } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GraphRoute } from '@/app/App';

type GraphWorkbenchModule = {
  default: ComponentType<{ apiBase: string }>;
};

const graphChunk = vi.hoisted(() => {
  let resolveChunk: (module: GraphWorkbenchModule) => void = () => undefined;
  let rejectChunk: (error: unknown) => void = () => undefined;
  let isSettled = false;

  function createPromise() {
    isSettled = false;
    const nextPromise = new Promise<GraphWorkbenchModule>((resolve, reject) => {
      resolveChunk = (module) => {
        isSettled = true;
        resolve(module);
      };
      rejectChunk = (error) => {
        isSettled = true;
        reject(error);
      };
    });
    nextPromise.catch(() => undefined);
    return nextPromise;
  }

  let promise = createPromise();

  return {
    reset() {
      promise = createPromise();
    },
    resolve(module: GraphWorkbenchModule) {
      resolveChunk(module);
    },
    reject(error: unknown) {
      rejectChunk(error);
    },
    load() {
      if (isSettled) {
        promise = createPromise();
      }

      return promise;
    }
  };
});

describe('Graph route shell', () => {
  beforeEach(() => {
    graphChunk.reset();
  });

  it('shows a visible loading panel while the graph route chunk loads', () => {
    render(<GraphRoute apiBase="/api-root" loadWorkbench={graphChunk.load} />);

    expect(screen.getByRole('heading', { name: 'Loading Knowledge Graph' })).toBeInTheDocument();
  });

  it('shows a recoverable graph route panel when the lazy graph route fails', async () => {
    render(<GraphRoute apiBase="/api-root" loadWorkbench={graphChunk.load} />);

    await act(async () => {
      graphChunk.reject(new Error('chunk failed'));
    });

    expect(await screen.findByRole('heading', { name: 'Knowledge Graph failed to load' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry graph route' }));
    graphChunk.resolve({
      default: () => <section aria-label="Mock graph route">Graph ready</section>
    });

    expect(await screen.findByText('Graph ready')).toBeInTheDocument();
  });
});
