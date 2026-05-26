import { ChevronLeft, ChevronRight } from 'lucide-react';

type PaginationProps = {
  page: number;
  pageSize: number;
  shownStart: number;
  shownEnd: number;
  totalCount: number;
  totalPages: number;
  isLoading?: boolean;
  pageSizeOptions?: number[];
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
};

type PageItem = number | 'ellipsis';

const defaultPageSizeOptions = [20, 50, 100];

export function Pagination({
  page,
  pageSize,
  shownStart,
  shownEnd,
  totalCount,
  totalPages,
  isLoading = false,
  pageSizeOptions = defaultPageSizeOptions,
  onPageChange,
  onPageSizeChange
}: PaginationProps) {
  const normalizedTotalPages = Math.max(1, totalPages);
  const visiblePageItems = getVisiblePageItems(page, normalizedTotalPages);

  return (
    <div className="lrn-pagination">
      <span className="lrn-pagination__result-count">
        Showing {shownStart} to {shownEnd} of {formatCount(totalCount)} results
      </span>
      <nav className="lrn-pagination__pages" aria-label="Pagination">
        <button
          type="button"
          className="lrn-pagination__button lrn-pagination__button--icon"
          aria-label="Previous page"
          disabled={page <= 1 || isLoading}
          onClick={() => onPageChange(Math.max(1, page - 1))}
        >
          <ChevronLeft size={16} aria-hidden="true" />
        </button>
        {visiblePageItems.map((item, index) => (
          item === 'ellipsis' ? (
            <span key={`ellipsis-${index}`} className="lrn-pagination__ellipsis" aria-hidden="true">
              ...
            </span>
          ) : (
            <button
              key={item}
              type="button"
              className={[
                'lrn-pagination__button',
                'lrn-pagination__page',
                item === page ? 'lrn-pagination__page--active' : undefined
              ].filter(Boolean).join(' ')}
              aria-label={`Go to page ${item}`}
              aria-current={item === page ? 'page' : undefined}
              disabled={isLoading}
              onClick={() => onPageChange(item)}
            >
              {item}
            </button>
          )
        ))}
        <button
          type="button"
          className="lrn-pagination__button lrn-pagination__button--icon"
          aria-label="Next page"
          disabled={page >= normalizedTotalPages || isLoading}
          onClick={() => onPageChange(Math.min(normalizedTotalPages, page + 1))}
        >
          <ChevronRight size={16} aria-hidden="true" />
        </button>
      </nav>
      <label className="lrn-pagination__page-size">
        <select
          aria-label="Rows per page"
          value={pageSize}
          disabled={isLoading}
          onChange={(event) => onPageSizeChange(Number(event.target.value))}
        >
          {pageSizeOptions.map((option) => (
            <option key={option} value={option}>
              {option} / page
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}

function getVisiblePageItems(page: number, totalPages: number): PageItem[] {
  if (totalPages <= 6) {
    return Array.from({ length: totalPages }, (_, index) => index + 1);
  }

  if (page <= 3) {
    return [1, 2, 3, 'ellipsis', totalPages];
  }

  if (page >= totalPages - 2) {
    return [1, 'ellipsis', totalPages - 2, totalPages - 1, totalPages];
  }

  return [1, 'ellipsis', page - 1, page, page + 1, 'ellipsis', totalPages];
}

function formatCount(value: number): string {
  return new Intl.NumberFormat('en-US').format(value);
}
