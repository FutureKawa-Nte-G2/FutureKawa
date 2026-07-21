import type { BatchStatus } from "@/lib/api/types";

interface BadgeProps {
  status: BatchStatus;
}

const STATUS_STYLES: Record<BatchStatus, { label: string; className: string }> = {
  compliant: { label: "compliant", className: "text-status-compliant-text bg-status-compliant-bg" },
  alert: { label: "alert", className: "text-status-alert-text bg-status-alert-bg" },
  expired: { label: "expired", className: "text-status-expired-text bg-status-expired-bg" },
};

export function Badge({ status }: BadgeProps) {
  const { label, className } = STATUS_STYLES[status];

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ${className}`}>
      <span className="h-1.5 w-1.5 rounded-full bg-current" />
      {label}
    </span>
  );
}