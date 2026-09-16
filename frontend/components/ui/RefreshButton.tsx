"use client";

import { useState } from "react";
import { useRefresh } from "@/context/RefreshContext";
import { useAuth } from "@/context/AuthContext";
import { syncMeasurements } from "@/lib/api/measurements";

export function RefreshButton() {
  const { triggerRefresh } = useRefresh();
  const { accessToken } = useAuth();
  const [isPending, setIsPending] = useState(false);
  const [syncError, setSyncError] = useState<string | null>(null);

  async function handleRefresh() {
    setIsPending(true);
    setSyncError(null);
    try {
      // Trigger a new measurement collection first. A failed collection
      // shouldn't block refreshing whatever is already in the database below
      // (see backlog-frontend-mesures-alertes-collecte.md) — caught here on
      // its own, separately from triggerRefresh().
      await syncMeasurements(accessToken);
    } catch {
      setSyncError("La collecte des mesures a échoué. Les données affichées n'ont pas été mises à jour.");
    }
    try {
      await triggerRefresh();
    } finally {
      setIsPending(false);
    }
  }

  return (
    <div className="relative">
      <button
        type="button"
        onClick={handleRefresh}
        disabled={isPending}
        aria-label="Rafraîchir les données"
        title="Rafraîchir les données"
        className="flex h-(--avatar-size) w-(--nav-icon-size) items-center justify-center rounded-full text-input-text hover:bg-background-secondary disabled:cursor-not-allowed disabled:opacity-50"
      >
        <RefreshIcon spinning={isPending} />
      </button>
      {syncError && (
        <p role="alert" className="absolute right-0 top-full z-10 mt-2 w-56 text-xs text-red-600">
          {syncError}
        </p>
      )}
    </div>
  );
}

function RefreshIcon({ spinning }: { spinning: boolean }) {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      className={spinning ? "animate-spin" : undefined}
    >
      <path d="M21 12a9 9 0 1 1-2.64-6.36" />
      <path d="M21 3v6h-6" />
    </svg>
  );
}