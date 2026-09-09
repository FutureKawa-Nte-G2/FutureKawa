"use client";

interface PaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  maxVisiblePages?: number; // default 5, matches the "1 2 3 4 5 ... 10" window from the mockup
}

const ELLIPSIS = "ellipsis" as const;

function getVisiblePages(
  currentPage: number,
  totalPages: number,
  maxVisiblePages: number
): (number | typeof ELLIPSIS)[] {
  if (totalPages <= maxVisiblePages + 1) {
    return Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  const half = Math.floor(maxVisiblePages / 2);
  let start = Math.max(1, currentPage - half);
  const end = Math.min(totalPages, start + maxVisiblePages - 1);
  start = Math.max(1, end - maxVisiblePages + 1);

  const pages: (number | typeof ELLIPSIS)[] = [];
  for (let page = start; page <= end; page++) pages.push(page);

  if (start > 1) pages.unshift(1, ELLIPSIS);
  if (end < totalPages) pages.push(ELLIPSIS, totalPages);

  return pages;
}

export function Pagination({ currentPage, totalPages, onPageChange, maxVisiblePages = 5 }: PaginationProps) {
  const pages = getVisiblePages(currentPage, totalPages, maxVisiblePages);

  return (
    <nav className="flex items-center gap-2" aria-label="Pagination">
      <button
        type="button"
        onClick={() => onPageChange(currentPage - 1)}
        disabled={currentPage === 1}
        aria-label="Page précédente"
        className="flex h-9 w-9 items-center justify-center rounded-[4px] border border-border-primary bg-background-secondary text-input-text disabled:cursor-not-allowed disabled:bg-transparent disabled:opacity-40"
      >
        &lt;
      </button>

      {pages.map((page, index) =>
        page === ELLIPSIS ? (
          <span key={`ellipsis-${index}`} className="flex h-9 w-9 items-center justify-center text-input-text">
            …
          </span>
        ) : (
          <button
            key={page}
            type="button"
            onClick={() => onPageChange(page)}
            aria-current={page === currentPage ? "page" : undefined}
            className={`flex h-9 w-9 items-center justify-center rounded-[4px] text-sm font-medium ${
              page === currentPage ? "bg-brand text-white" : "text-foreground hover:bg-background-secondary"
            }`}
          >
            {page}
          </button>
        )
      )}

      <button
        type="button"
        onClick={() => onPageChange(currentPage + 1)}
        disabled={currentPage === totalPages}
        aria-label="Page suivante"
        className="flex h-9 w-9 items-center justify-center rounded-[4px] border border-border-primary bg-background-secondary text-input-text disabled:cursor-not-allowed disabled:bg-transparent disabled:opacity-40"
      >
        &gt;
      </button>
    </nav>
  );
}