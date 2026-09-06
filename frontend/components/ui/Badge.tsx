import type { BatchStatus } from '@/lib/api/types';

interface BadgeProps {
  status: BatchStatus;
}

// Displayed labels are in French; BatchStatus values themselves stay as the English API contract
const STATUS_STYLES: Record<BatchStatus, { label: string; className: string }> =
  {
    compliant: {
      label: 'conforme',
      className: 'text-status-compliant-text bg-status-compliant-bg',
    },
    expired: {
      label: 'périmé',
      className: 'text-status-expired-text bg-status-expired-bg',
    },
  };

export function Badge({ status }: BadgeProps) {
  const { label, className } = STATUS_STYLES[status];

  return (
    <span
      className={`inline-flex h-8 w-24 shrink-0 items-center justify-center rounded-full text-center text-xs font-medium ${className}`}
    >
      {label}
    </span>
  );
}
