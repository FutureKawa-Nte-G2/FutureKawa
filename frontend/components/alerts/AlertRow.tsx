"use client";

import { useState } from "react";
import type { Alert, AlertType } from "@/lib/api/types";
import { AlertStatusBadge } from "./AlertStatusBadge";
import { Button } from "@/components/ui/Button";
import { GRID_TEMPLATE, ROW_CLASSES } from "./grid";

const TYPE_LABELS: Record<AlertType, string> = {
  temperature: "Température",
  humidity: "Humidité",
};

interface AlertRowProps {
  alert: Alert;
}

export function AlertRow({ alert }: AlertRowProps) {
  const [isExpanded, setIsExpanded] = useState(false);

  return (
    <>
      <div className={`${ROW_CLASSES} ${GRID_TEMPLATE}`}>
        <span className="min-w-0 text-center text-sm text-foreground">
          {alert.warehouseName} ({alert.countryName})
        </span>
        <span className="min-w-0 text-center text-sm text-foreground">{formatDate(alert.createdAt)}</span>
        <span className="min-w-0 text-center text-sm text-foreground">
          {alert.resolvedAt ? formatDate(alert.resolvedAt) : "—"}
        </span>
        <AlertStatusBadge status={alert.status} />
        <span className="min-w-0 text-center text-sm text-foreground">{TYPE_LABELS[alert.type]}</span>
        <Button variant="primary" onClick={() => setIsExpanded((v) => !v)}>
          Liste des lots
          <ChevronIcon expanded={isExpanded} />
        </Button>
        <Button variant="alert" disabled={alert.status === "resolved"}>
          Acquitter
        </Button>
      </div>
      {isExpanded && (
        <div className="w-full max-w-[990px] rounded-[4px] border border-border-primary bg-background-secondary px-4 py-3 text-sm text-input-text">
          Chargement des lots concernés à venir.
        </div>
      )}
    </>
  );
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("fr-FR", { year: "numeric", month: "short", day: "numeric" });
}

function ChevronIcon({ expanded }: { expanded: boolean }) {
  return (
    <svg
      width="12"
      height="12"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      className={`ml-2 transition-transform ${expanded ? "rotate-180" : ""}`}
    >
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}
