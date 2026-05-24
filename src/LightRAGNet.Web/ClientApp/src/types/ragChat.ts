export type QueryMode = "Local" | "Global" | "Hybrid" | "Naive" | "Mix" | "Bypass";

export type DebugOutputMode = "Answer" | "ContextOnly" | "PromptOnly";

export type RagQueryRequest = {
  query: string;
  mode: QueryMode;
  stream: boolean;
  includeReferences: boolean;
  responseType: string;
  topK: number;
  chunkTopK: number;
  enableRerank: boolean;
  highLevelKeywords: string[];
  lowLevelKeywords: string[];
  onlyNeedContext: boolean;
  onlyNeedPrompt: boolean;
};

export type RagQueryReference = {
  referenceId: string;
  filePath: string;
  fileName: string;
  previewUrl?: string | null;
  openKind: ReferenceOpenKind;
};

export type ReferenceOpenKind =
  | "DocumentPreview"
  | "ConvertedMarkdown"
  | "OriginalArtifact"
  | "UploadedFile"
  | "ExternalOrUnresolved";

export type QueryMetadataEvent = {
  type: "metadata";
  mode: QueryMode;
  stream: boolean;
  includeReferences: boolean;
  responseType: string;
  cachePolicy: string;
  references: RagQueryReference[];
  highLevelKeywords: string[];
  lowLevelKeywords: string[];
  diagnostics: Record<string, string>;
};

export type RagQueryEvent =
  | { type: "text_chunk"; chunk: string }
  | { type: "error"; error: string; message?: string | null }
  | { type: "done" }
  | QueryMetadataEvent;

export type RagQueryDataResponse = {
  status: string;
  message: string;
  data: Record<string, unknown>;
  metadata: Record<string, unknown>;
};

export type ChatMessage = {
  id: string;
  role: "User" | "Assistant";
  text: string;
  request?: RagQueryRequest;
  metadata?: QueryMetadataEvent;
  retrievalData?: RagQueryDataResponse;
  isComplete: boolean;
  isStreaming: boolean;
  isLoadingRetrievalData: boolean;
  errorMessage?: string;
};
