"use client";

import { useState } from "react";
import { useRefresh } from "@/context/RefreshContext";

export function RefreshButton() {
  const { triggerRefresh } = useRefresh();
  const [isPending, setIsPending] = useState(false);

  async function handleRefresh() {
    setIsPending(true);
    try {
      await triggerRefresh();
    } finally {
      setIsPending(false);
    }
  }

  return (
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