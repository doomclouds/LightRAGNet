import type { ChatMessage } from "../types/ragChat";
import { AssistantMessage } from "./AssistantMessage";

type Props = {
  messages: ChatMessage[];
  input: string;
  isRunning: boolean;
  onInputChange: (value: string) => void;
  onSend: () => void;
  onOpenDetails: (message: ChatMessage) => void;
};

export function ChatPane({ messages, input, isRunning, onInputChange, onSend, onOpenDetails }: Props) {
  return (
    <section className="rag-chat__chat lrn-panel">
      <div className="rag-chat__messages">
        {messages.length === 0 ? <div className="rag-chat__empty">Ask a question to start.</div> : null}
        {messages.map((message) =>
          message.role === "Assistant" ? (
            <AssistantMessage key={message.id} message={message} onOpenDetails={() => onOpenDetails(message)} />
          ) : (
            <article key={message.id} className="rag-chat__message rag-chat__message--user">
              {message.text}
            </article>
          )
        )}
      </div>

      <div className="rag-chat__composer" data-testid="rag-chat-composer">
        <textarea
          aria-label="Message"
          className="lrn-textarea rag-chat__input"
          disabled={isRunning}
          placeholder="Ask RAG..."
          value={input}
          onChange={(event) => onInputChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              onSend();
            }
          }}
        />
        <button className="lrn-button lrn-button--accent" type="button" disabled={isRunning || !input.trim()} onClick={onSend}>
          Send
        </button>
      </div>
    </section>
  );
}
