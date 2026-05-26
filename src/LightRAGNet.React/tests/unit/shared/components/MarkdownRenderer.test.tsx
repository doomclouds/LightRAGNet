import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MarkdownRenderer } from '@/shared/components/MarkdownRenderer';

const mermaidMock = vi.hoisted(() => ({
  initialize: vi.fn(),
  render: vi.fn()
}));

vi.mock('mermaid', () => ({
  default: mermaidMock
}));

describe('MarkdownRenderer', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders Mermaid fenced code blocks as diagrams with strict manual initialization', async () => {
    mermaidMock.render.mockResolvedValue({
      svg: '<svg role="img" aria-label="Flowchart"><text>Rendered diagram</text></svg>'
    });

    render(
      <MarkdownRenderer
        content={[
          '# Architecture',
          '',
          '```mermaid',
          'graph TD',
          '  A[Upload] --> B[Index]',
          '```'
        ].join('\n')}
      />
    );

    expect(await screen.findByText('Rendered diagram')).toBeInTheDocument();
    expect(mermaidMock.initialize).toHaveBeenCalledWith(
      expect.objectContaining({
        startOnLoad: false,
        securityLevel: 'strict'
      })
    );
    expect(mermaidMock.render).toHaveBeenCalledWith(expect.stringMatching(/^lrn-mermaid-/), 'graph TD\n  A[Upload] --> B[Index]');
  });

  it('falls back to the original Mermaid source when rendering fails', async () => {
    mermaidMock.render.mockRejectedValue(new Error('Parse failed'));

    render(
      <MarkdownRenderer
        content={[
          '```mermaid',
          'graph TD',
          '  A -->',
          '```'
        ].join('\n')}
      />
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to render Mermaid diagram.');
    expect(screen.getByText('Parse failed')).toBeInTheDocument();
    expect(screen.getByText(/graph TD/)).toBeInTheDocument();
    expect(screen.getByText(/A -->/)).toBeInTheDocument();
  });

  it('escapes sequence diagram semicolons before rendering while keeping the original fallback source', async () => {
    mermaidMock.render.mockResolvedValue({
      svg: '<svg role="img"><text>Sequence diagram</text></svg>'
    });

    render(
      <MarkdownRenderer
        content={[
          '```mermaid',
          'sequenceDiagram',
          '    participant DC as CCUV3500DeviceController',
          '    DC->>DC: _dataManager.Reset(); _UploadData=false',
          '```'
        ].join('\n')}
      />
    );

    expect(await screen.findByText('Sequence diagram')).toBeInTheDocument();
    expect(mermaidMock.render).toHaveBeenCalledWith(
      expect.stringMatching(/^lrn-mermaid-/),
      'sequenceDiagram\n    participant DC as CCUV3500DeviceController\n    DC->>DC: _dataManager.Reset()#59; _UploadData=false'
    );
  });

  it('keeps non-Mermaid code blocks as code', async () => {
    render(
      <MarkdownRenderer
        content={[
          '```csharp',
          'Console.WriteLine("hello");',
          '```'
        ].join('\n')}
      />
    );

    expect(await screen.findByText('Console.WriteLine("hello");')).toBeInTheDocument();
    await waitFor(() => expect(mermaidMock.render).not.toHaveBeenCalled());
  });
});
