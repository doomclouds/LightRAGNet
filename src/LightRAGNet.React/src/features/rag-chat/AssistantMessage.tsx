import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

import type { ChatMessage, RagQueryReference } from "@/types/ragChat";

type Props = {
  message: ChatMessage;
  onOpenDetails: () => void;
};

export function AssistantMessage({ message, onOpenDetails }: Props) {
  const metadata = message.metadata;
  const references = metadata?.references ?? [];

  return (
    <article className="rag-chat__message rag-chat__message--assistant">
      <div className="rag-chat__markdown">
        <ReactMarkdown remarkPlugins={[remarkGfm]}>
          {message.text || (message.isComplete ? "No content returned." : "")}
        </ReactMarkdown>
      </div>

      {!message.isComplete ? <div className="rag-chat__loading">Generating...</div> : null}
      {message.errorMessage ? <div className="rag-chat__error">{message.errorMessage}</div> : null}

      {metadata ? (
        <div className="rag-chat__message-meta">
          <span className="lrn-chip">{metadata.mode}</span>
          <span className="lrn-chip">{metadata.stream ? "Streaming" : "Non-stream"}</span>
          <span className="lrn-chip">{metadata.cachePolicy || "Cacheable"}</span>
        </div>
      ) : null}

      {references.length ? (
        <div className="rag-chat__references" aria-label="References">
          {references.map((reference) => {
            const href = getReferenceHref(reference);

            return href ? (
              <a key={reference.referenceId} href={href} target="_blank" rel="noopener noreferrer">
                {reference.fileName || reference.filePath}
              </a>
            ) : (
              <span key={reference.referenceId}>{reference.fileName || reference.filePath}</span>
            );
          })}
        </div>
      ) : null}

      {message.isComplete && message.request ? (
        <div className="rag-chat__message-actions">
          <button className="lrn-button" type="button" onClick={onOpenDetails}>
            View query details
          </button>
        </div>
      ) : null}
    </article>
  );
}

function getReferenceHref(reference: RagQueryReference): string | null {
  if (!reference.previewUrl) {
    return null;
  }

  if (reference.openKind !== "DocumentPreview") {
    return reference.previewUrl;
  }

  try {
    const url = new URL(reference.previewUrl, window.location.origin);
    const match = url.pathname.match(/(?:^|\/)document-preview\/(\d+)$/);
    return match ? `/document-preview/${match[1]}` : reference.previewUrl;
  } catch {
    return reference.previewUrl;
  }
}
