import { Paperclip, Send, Sparkles, UserRound } from "lucide-react";

import type { ChatMessage } from "@/types/ragChat";
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
    <section className={`rag-chat__chat ${messages.length === 0 ? "rag-chat__chat--empty" : ""}`} aria-label="RAG conversation">
      <div className="rag-chat__messages" data-scroll-surface="messages">
        {messages.length === 0 ? (
          <div className="rag-chat__empty">
            <span className="rag-chat__empty-icon" aria-hidden="true">
              <Sparkles size={18} />
            </span>
            <div>
              <strong>Ask a question to start.</strong>
              <p>Use the settings panel to tune retrieval depth, references and response shape.</p>
            </div>
          </div>
        ) : null}
        {messages.map((message) =>
          message.role === "Assistant" ? (
            <AssistantMessage key={message.id} message={message} onOpenDetails={() => onOpenDetails(message)} />
          ) : (
            <article key={message.id} className="rag-chat__message rag-chat__message--user">
              <div className="rag-chat__message-head">
                <span className="rag-chat__avatar rag-chat__avatar--user" aria-hidden="true">
                  <UserRound size={15} />
                </span>
                <strong>You</strong>
                <span>Now</span>
              </div>
              <p>{message.text}</p>
            </article>
          )
        )}
      </div>

      <div className="rag-chat__composer" data-testid="rag-chat-composer">
        <div className="rag-chat__composer-tools">
          <button className="rag-chat__auto-mode" type="button" aria-pressed="true" disabled={isRunning}>
            Auto
          </button>
          <button className="rag-chat__composer-icon" type="button" aria-label="Attach context" disabled={isRunning}>
            <Paperclip size={16} aria-hidden="true" />
          </button>
        </div>
        <textarea
          aria-label="Message"
          className="lrn-textarea rag-chat__input"
          disabled={isRunning}
          placeholder="Ask anything about your knowledge base..."
          value={input}
          onChange={(event) => onInputChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              onSend();
            }
          }}
        />
        <button className="rag-chat__send-action" type="button" disabled={isRunning || !input.trim()} onClick={onSend}>
          <Send size={16} aria-hidden="true" />
          <span className="rag-chat__sr-only">Send</span>
        </button>
      </div>
    </section>
  );
}
