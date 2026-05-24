type StatusLike = {
  ragStatus?: string | null;
};

type TaskStatusLike = {
  status?: string | null;
};

export function normalizeFilterStatus(status?: string | null): string | undefined {
  if (!status) {
    return undefined;
  }

  return status === 'Pending' ? 'Queued' : status;
}

export function shouldRefreshForTaskStatus(
  update: TaskStatusLike,
  oldStatus?: string | null,
  selectedStatusFilter?: string | null
): boolean {
  const normalizedOldStatus = normalizeFilterStatus(oldStatus);
  const normalizedUpdateStatus = normalizeFilterStatus(update.status);
  const normalizedSelectedStatus = normalizeFilterStatus(selectedStatusFilter);

  if (normalizedSelectedStatus) {
    return normalizedOldStatus !== normalizedUpdateStatus &&
      (normalizedOldStatus === normalizedSelectedStatus || normalizedUpdateStatus === normalizedSelectedStatus);
  }

  return isFinalTaskStatus(normalizedUpdateStatus) && isActiveTaskStatus(normalizedOldStatus);
}

export function shouldRefreshForMissingTaskStatus(
  update: TaskStatusLike,
  selectedStatusFilter?: string | null
): boolean {
  const normalizedSelectedStatus = normalizeFilterStatus(selectedStatusFilter);

  return Boolean(normalizedSelectedStatus) &&
    normalizeFilterStatus(update.status) === normalizedSelectedStatus;
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

function isActiveTaskStatus(status?: string): boolean {
  return status === 'Queued' || status === 'Processing';
}

function isFinalTaskStatus(status?: string): boolean {
  return status === 'Completed' || status === 'Failed';
}
