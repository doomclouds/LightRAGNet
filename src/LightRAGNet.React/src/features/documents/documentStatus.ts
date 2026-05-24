type StatusLike = {
  ragStatus?: string | null;
};

export function normalizeFilterStatus(status?: string | null): string | undefined {
  if (!status) {
    return undefined;
  }

  return status === 'Pending' ? 'Queued' : status;
}

export function isDocumentBusy(document: StatusLike): boolean {
  return document.ragStatus === 'Pending' ||
    document.ragStatus === 'Queued' ||
    document.ragStatus === 'Processing' ||
    document.ragStatus === 'Deleting';
}

export function canRetryDocument(document: StatusLike): boolean {
  return document.ragStatus === 'Failed' || document.ragStatus === 'Cancelled';
}

export function canCancelDocumentPipeline(document: StatusLike): boolean {
  return document.ragStatus === 'Queued' ||
    document.ragStatus === 'Processing' ||
    document.ragStatus === 'Pending';
}

export function getShortErrorMessage(errorMessage?: string | null): string {
  if (!errorMessage || errorMessage.trim().length === 0) {
    return 'Unknown error';
  }

  return errorMessage.length <= 120 ? errorMessage : `${errorMessage.slice(0, 120)}...`;
}
