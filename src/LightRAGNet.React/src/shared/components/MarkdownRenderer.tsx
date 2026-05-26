import { Children, isValidElement, useEffect, useMemo, useState, type ReactNode } from 'react';
import ReactMarkdown, { type Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';

type MarkdownRendererProps = {
  content: string;
  className?: string;
};

type MermaidState =
  | { status: 'loading' }
  | { status: 'rendered'; svg: string }
  | { status: 'error' };

let mermaidId = 0;

const markdownComponents: Components = {
  pre({ children, ...props }) {
    const mermaidChart = getMermaidChart(children);

    if (mermaidChart !== null) {
      return <MermaidBlock chart={mermaidChart} />;
    }

    return <pre {...props}>{children}</pre>;
  }
};

export function MarkdownRenderer({ content, className }: MarkdownRendererProps) {
  return (
    <div className={['lrn-markdown', className].filter(Boolean).join(' ')}>
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents}>
        {content}
      </ReactMarkdown>
    </div>
  );
}

function MermaidBlock({ chart }: { chart: string }) {
  const diagramId = useMemo(() => `lrn-mermaid-${++mermaidId}`, [chart]);
  const [state, setState] = useState<MermaidState>({ status: 'loading' });

  useEffect(() => {
    let isActive = true;

    setState({ status: 'loading' });

    import('mermaid')
      .then(async (module) => {
        const mermaid = module.default;
        mermaid.initialize({
          startOnLoad: false,
          securityLevel: 'strict',
          theme: 'default'
        });

        return mermaid.render(diagramId, chart);
      })
      .then((result) => {
        if (isActive) {
          setState({ status: 'rendered', svg: result.svg });
        }
      })
      .catch(() => {
        if (isActive) {
          setState({ status: 'error' });
        }
      });

    return () => {
      isActive = false;
    };
  }, [chart, diagramId]);

  return (
    <figure className="lrn-mermaid" aria-label="Mermaid diagram">
      {state.status === 'loading' ? <p className="lrn-mermaid__state">Rendering diagram...</p> : null}
      {state.status === 'rendered' ? (
        <div className="lrn-mermaid__svg" dangerouslySetInnerHTML={{ __html: state.svg }} />
      ) : null}
      {state.status === 'error' ? (
        <div className="lrn-mermaid__fallback">
          <p role="alert">Unable to render Mermaid diagram.</p>
          <pre>
            <code>{chart}</code>
          </pre>
        </div>
      ) : null}
    </figure>
  );
}

function getMermaidChart(children: ReactNode): string | null {
  const child = Children.toArray(children)[0];

  if (!isValidElement(child)) {
    return null;
  }

  const props = child.props as { className?: string; children?: ReactNode };

  if (!/\blanguage-mermaid\b/i.test(props.className ?? '')) {
    return null;
  }

  return Children.toArray(props.children).join('').replace(/\n$/, '');
}
