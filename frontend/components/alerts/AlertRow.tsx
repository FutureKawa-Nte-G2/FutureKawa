"use client";

import { useEffect, useState } from "react";
import { getAlertBatches, resolveAlert } from "@/lib/api/alerts";
import type { Alert, AlertBatch, AlertType } from "@/lib/api/types";
import { AlertStatusBadge } from "./AlertStatusBadge";
import { Button } from "@/components/ui/Button";
import { useAuth } from "@/context/AuthContext";
import { GRID_TEMPLATE, ROW_CLASSES } from "./grid";

const TYPE_LABELS: Record<AlertType, string> = {
  temperature: "Température",
  humidity: "Humidité",
};

interface AlertRowProps {
  alert: Alert;
  onResolved?: () => void;
}

export function AlertRow({ alert, onResolved }: AlertRowProps) {
  const [isExpanded, setIsExpanded] = useState(false);
  const [batches, setBatches] = useState<AlertBatch[] | null>(null);
  const [isLoadingBatches, setIsLoadingBatches] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isResolving, setIsResolving] = useState(false);
  const [resolveError, setResolveError] = useState<string | null>(null);
  const { accessToken } = useAuth();

  async function handleAcquitter() {
    setIsResolving(true);
    setResolveError(null);
    try {
      await resolveAlert(alert.id, accessToken);
      onResolved?.();
    } catch {
      setResolveError("Impossible d'acquitter cette alerte.");
    } finally {
      setIsResolving(false);
    }
  }

  useEffect(() => {
    if (!isExpanded || batches !== null) return;

    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIsLoadingBatches(true);
    setError(null);

    getAlertBatches(alert.id, accessToken)
      .then((result) => {
        if (!cancelled) setBatches(result);
      })
      .catch(() => {
        if (!cancelled) setError("Impossible de charger les lots concernés.");
      })
      .finally(() => {
        if (!cancelled) setIsLoadingBatches(false);
      });

    return () => {
      cancelled = true;
    };
  }, [isExpanded, batches, alert.id, accessToken]);

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
        <Button variant="secondary" onClick={() => setIsExpanded((v) => !v)}>
          Liste des lots
          <ChevronIcon expanded={isExpanded} />
        </Button>
        <Button
          variant={alert.status === "resolved" ? "resolved" : "alert"}
          disabled={alert.status === "resolved" || isResolving}
          onClick={handleAcquitter}
        >
          {alert.status === "resolved" ? "Acquitté" : "Acquitter"}
        </Button>
      </div>
      {resolveError && (
        <p role="alert" className="text-sm text-red-600">
          {resolveError}
        </p>
      )}
      {isExpanded && (
        <div className="w-full max-w-[990px] rounded-[4px] border border-border-primary bg-background-secondary px-4 py-3 text-sm text-input-text">
          {isLoadingBatches && <p>Chargement des lots...</p>}
          {!isLoadingBatches && error && (
            <p role="alert" className="text-red-600">
              {error}
            </p>
          )}
          {!isLoadingBatches && !error && batches?.length === 0 && <p>Aucun lot concerné.</p>}
          {!isLoadingBatches && !error && batches && batches.length > 0 && (
            <ul className="flex flex-col gap-2">
              {batches.map((batch) => (
                <li key={batch.id} className="flex items-center gap-6 text-foreground">
                  <span className="w-32 font-medium">{batch.batchRef}</span>
                  <span className="w-40">{batch.farmName}</span>
                  <span className="w-8">{batch.qualityGrade}</span>
                  <span>{formatDate(batch.enteredAt)}</span>
                </li>
              ))}
            </ul>
          )}
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
