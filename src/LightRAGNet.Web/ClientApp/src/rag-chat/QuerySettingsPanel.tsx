import type { DebugOutputMode, QueryMode } from "../types/ragChat";
import type { QuerySettings } from "./ragChatSettings";

type Props = {
  settings: QuerySettings;
  disabled: boolean;
  onChange: (settings: QuerySettings) => void;
};

const modes: QueryMode[] = ["Mix", "Naive", "Bypass", "Local", "Global", "Hybrid"];
const responseTypes = ["Multiple Paragraphs", "Single Paragraph", "Bullet Points", "Concise"];
const debugOutputModes: DebugOutputMode[] = ["Answer", "ContextOnly", "PromptOnly"];

export function QuerySettingsPanel({ settings, disabled, onChange }: Props) {
  const isBypass = settings.mode === "Bypass";
  const retrievalDisabled = disabled || isBypass;

  return (
    <aside className="rag-chat__settings lrn-panel">
      <div className="lrn-panel__head">
        <h2>Query settings</h2>
      </div>

      <div className="rag-chat__settings-body">
        <label className="rag-chat__field">
          <span>Mode</span>
          <select
            aria-label="Mode"
            className="lrn-select"
            disabled={disabled}
            value={settings.mode}
            onChange={(event) => onChange({ ...settings, mode: event.target.value as QueryMode })}
          >
            {modes.map((mode) => (
              <option key={mode} value={mode}>
                {mode}
              </option>
            ))}
          </select>
        </label>

        <label className="rag-chat__field">
          <span>Response</span>
          <select
            aria-label="Response"
            className="lrn-select"
            disabled={disabled}
            value={settings.responseType}
            onChange={(event) => onChange({ ...settings, responseType: event.target.value })}
          >
            {responseTypes.map((responseType) => (
              <option key={responseType} value={responseType}>
                {responseType}
              </option>
            ))}
          </select>
        </label>

        <label className="rag-chat__switch">
          <input
            aria-label="Streaming"
            type="checkbox"
            disabled={disabled}
            checked={settings.streamResponse}
            onChange={(event) => onChange({ ...settings, streamResponse: event.target.checked })}
          />
          <span>Streaming</span>
        </label>

        <label className="rag-chat__switch">
          <input
            aria-label="References"
            type="checkbox"
            disabled={retrievalDisabled}
            checked={!isBypass && settings.includeReferences}
            onChange={(event) => onChange({ ...settings, includeReferences: event.target.checked })}
          />
          <span>References</span>
        </label>

        <label className="rag-chat__switch">
          <input
            aria-label="Rerank"
            type="checkbox"
            disabled={retrievalDisabled}
            checked={!isBypass && settings.enableRerank}
            onChange={(event) => onChange({ ...settings, enableRerank: event.target.checked })}
          />
          <span>Rerank</span>
        </label>

        <div className="rag-chat__number-grid">
          <label className="rag-chat__field">
            <span>TopK</span>
            <input
              aria-label="TopK"
              className="lrn-input"
              type="number"
              min={1}
              max={200}
              disabled={retrievalDisabled}
              value={settings.topK}
              onChange={(event) => onChange({ ...settings, topK: Number(event.target.value) })}
            />
          </label>

          <label className="rag-chat__field">
            <span>ChunkTopK</span>
            <input
              aria-label="ChunkTopK"
              className="lrn-input"
              type="number"
              min={1}
              max={200}
              disabled={retrievalDisabled}
              value={settings.chunkTopK}
              onChange={(event) => onChange({ ...settings, chunkTopK: Number(event.target.value) })}
            />
          </label>
        </div>

        <label className="rag-chat__field">
          <span>High keywords</span>
          <input
            aria-label="High keywords"
            className="lrn-input"
            disabled={retrievalDisabled}
            value={settings.highLevelKeywordsText}
            onChange={(event) => onChange({ ...settings, highLevelKeywordsText: event.target.value })}
          />
        </label>

        <label className="rag-chat__field">
          <span>Low keywords</span>
          <input
            aria-label="Low keywords"
            className="lrn-input"
            disabled={retrievalDisabled}
            value={settings.lowLevelKeywordsText}
            onChange={(event) => onChange({ ...settings, lowLevelKeywordsText: event.target.value })}
          />
        </label>

        <label className="rag-chat__field">
          <span>Debug output</span>
          <select
            aria-label="Debug output"
            className="lrn-select"
            disabled={disabled}
            value={settings.debugOutputMode}
            onChange={(event) => onChange({ ...settings, debugOutputMode: event.target.value as DebugOutputMode })}
          >
            {debugOutputModes.map((mode) => (
              <option key={mode} value={mode}>
                {mode}
              </option>
            ))}
          </select>
        </label>
      </div>
    </aside>
  );
}
