import type { AlertStatus } from "@/lib/api/types";

interface AlertStatusBadgeProps {
  status: AlertStatus;
}

const STATUS_STYLES: Record<AlertStatus, { label: string; className: string }> = {
  active: {
    label: "active",
    className: "text-status-alert-text bg-status-alert-bg",
  },
  resolved: {
    label: "résolue",
    className: "text-status-compliant-text bg-status-compliant-bg",
  },
};

export function AlertStatusBadge({ status }: AlertStatusBadgeProps) {
  const { label, className } = STATUS_STYLES[status];

  return (
    <span
      className={`inline-flex h-8 w-24 shrink-0 items-center justify-center rounded-full text-center text-xs font-medium ${className}`}
    >
      {label}
    </span>
  );
}
