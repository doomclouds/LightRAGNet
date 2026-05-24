export type MarkdownDocumentDto = {
  id: number;
  fileName: string;
  content?: string | null;
  fileSize: number;
  uploadTime: string;
  lastModified?: string | null;
  isInRagSystem: boolean;
  ragAddedTime?: string | null;
  ragStatus?: string | null;
  trackId?: string | null;
  ragProgress: number;
  ragCurrentStage?: string | null;
  activeRagTaskId?: string | null;
  ragRetryCount: number;
  ragErrorMessage?: string | null;
  ragDocumentId?: string | null;
  fileUrl?: string | null;
  originalFileName?: string | null;
  originalContentType?: string | null;
  conversionStatus?: string | null;
  conversionErrorMessage?: string | null;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

export type DocumentSubmissionResponse = {
  trackId: string;
  documents: MarkdownDocumentDto[];
};

export type DocumentPipelineActionResult = {
  accepted: boolean;
  documentId: number;
  status: string;
  message?: string | null;
};

export type MarkdownDocumentDeleteClientResult = {
  succeeded?: boolean;
  deletedImmediately?: boolean;
  accepted?: boolean;
  conflict?: boolean;
  taskId?: string | null;
  errorMessage?: string | null;
};

export type TaskStatusUpdate = {
  documentId: number;
  status: string;
  operationType?: string | null;
  currentStage?: string | null;
  progress: number;
  errorMessage?: string | null;
  completedAt?: string | null;
};
