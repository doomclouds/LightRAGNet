import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

import type { ChatMessage } from "../types/ragChat";

type Props = {
  message: ChatMessage;
  onOpenDetails: () => void;
};

export function AssistantMessage({ message, onOpenDetails }: Props) {
  const metadata = message.metadata;
  const previewReferences = metadata?.references.filter((reference) => reference.previewUrl) ?? [];

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

      {previewReferences.length ? (
        <div className="rag-chat__references" aria-label="References">
          {previewReferences.map((reference) => (
            <a key={reference.referenceId} href={reference.previewUrl ?? ""} target="_blank" rel="noopener noreferrer">
              {reference.fileName || reference.filePath}
            </a>
          ))}
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
