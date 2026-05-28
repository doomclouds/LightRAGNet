import type { ReactNode } from "react";
import { RotateCcw, SlidersHorizontal } from "lucide-react";
import type { DebugOutputMode, QueryMode } from "@/types/ragChat";
import { defaultQuerySettings, type QuerySettings } from "./ragChatSettings";

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
    <aside className="rag-chat__settings">
      <div className="rag-chat__settings-head">
        <span className="rag-chat__settings-icon" aria-hidden="true">
          <SlidersHorizontal size={18} strokeWidth={2.2} />
        </span>
        <div>
          <h2>Query Settings</h2>
          <p>Balance retrieval, graph expansion and diagnostics.</p>
        </div>
      </div>

      <div className="rag-chat__settings-body">
        <SettingRow label="Mode" note="Retrieval route and graph blend.">
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
        </SettingRow>

        <SettingRow label="Response style" note="Shape the answer before it is rendered.">
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
        </SettingRow>

        <div className="rag-chat__toggle-stack">
          <SwitchRow
            label="Streaming"
            description="Stream the answer as tokens arrive."
            disabled={disabled}
            checked={settings.streamResponse}
            onChange={(checked) => onChange({ ...settings, streamResponse: checked })}
          />
          <SwitchRow
            label="References"
            description="Surface source previews when metadata is available."
            disabled={retrievalDisabled}
            checked={!isBypass && settings.includeReferences}
            onChange={(checked) => onChange({ ...settings, includeReferences: checked })}
          />
          <SwitchRow
            label="Rerank"
            description="Use the reranker to sharpen retrieved context."
            disabled={retrievalDisabled}
            checked={!isBypass && settings.enableRerank}
            onChange={(checked) => onChange({ ...settings, enableRerank: checked })}
          />
        </div>

        <div className="rag-chat__number-grid">
          <SettingRow label="TopK" note="Number of chunks to retrieve." inline>
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
          </SettingRow>

          <SettingRow label="ChunkTopK" note="Chunks per document." inline>
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
          </SettingRow>
        </div>

        <SettingRow label="Keywords" note="Bias retrieval toward important terms. Optional.">
          <input
            aria-label="High keywords"
            className="lrn-input"
            disabled={retrievalDisabled}
            value={settings.highLevelKeywordsText}
            onChange={(event) => onChange({ ...settings, highLevelKeywordsText: event.target.value })}
          />
        </SettingRow>

        <SettingRow label="Exclude keywords" note="Filter out noisy concepts. Optional.">
          <input
            aria-label="Low keywords"
            className="lrn-input"
            disabled={retrievalDisabled}
            value={settings.lowLevelKeywordsText}
            onChange={(event) => onChange({ ...settings, lowLevelKeywordsText: event.target.value })}
          />
        </SettingRow>

        <SettingRow label="Debug output" note="Choose answer, context, or prompt inspection.">
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
        </SettingRow>

        <button className="rag-chat__reset-action" type="button" disabled={disabled} onClick={() => onChange(defaultQuerySettings)}>
          <RotateCcw size={16} strokeWidth={2.2} />
          Reset to defaults
        </button>
      </div>
    </aside>
  );
}

type SettingRowProps = {
  label: string;
  note: string;
  children: ReactNode;
  inline?: boolean;
};

function SettingRow({ label, note, children, inline = false }: SettingRowProps) {
  return (
    <label className={inline ? "rag-chat__setting-row rag-chat__setting-row--inline" : "rag-chat__setting-row"}>
      <span className="rag-chat__field-copy">
        <span className="rag-chat__field-title">{label}</span>
        <span className="rag-chat__field-note">{note}</span>
      </span>
      {children}
    </label>
  );
}

type SwitchRowProps = {
  label: string;
  description: string;
  disabled: boolean;
  checked: boolean;
  onChange: (checked: boolean) => void;
};

function SwitchRow({ label, description, disabled, checked, onChange }: SwitchRowProps) {
  return (
    <label className="rag-chat__switch">
      <span className="rag-chat__switch-copy">
        <span className="rag-chat__switch-title">{label}</span>
        <span className="rag-chat__switch-description">{description}</span>
      </span>
      <input
        aria-label={label}
        type="checkbox"
        disabled={disabled}
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span className="rag-chat__switch-track" aria-hidden="true" />
    </label>
  );
}
