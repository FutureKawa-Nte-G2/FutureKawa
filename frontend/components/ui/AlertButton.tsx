"use client";

import { useEffect, useState } from "react";
import { getUnreadAlerts, markAlertAsRead } from "@/lib/api/alerts";
import type { AlertItem } from "@/lib/api/types";

const KIND_STYLES: Record<AlertItem["kind"], string> = {
  alert: "text-status-alert-text bg-status-alert-bg",
  expired: "text-status-expired-text bg-status-expired-bg",
};

export function AlertButton() {
  const [alerts, setAlerts] = useState<AlertItem[]>([]);
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    getUnreadAlerts().then(setAlerts);
  }, []);

  async function handleAlertClick(id: string) {
    setAlerts((prev) => prev.filter((a) => a.id !== id));
    await markAlertAsRead(id);
  }

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-label={`Alertes non lues (${alerts.length})`}
        className="relative flex h-(--nav-icon-size) w-(--nav-icon-size) items-center justify-center rounded-full bg-background-secondary text-input-text hover:bg-brand-hover/10"
      >
        <BellIcon />
        {alerts.length > 0 && (
          <span className="absolute -right-1 -top-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-status-alert-text px-1 text-[10px] font-medium text-white">
            {alerts.length}
          </span>
        )}
      </button>

      {isOpen && (
        <div
          role="menu"
          className="absolute right-0 top-full mt-2 w-80 rounded-lg border border-border-primary bg-background py-1 shadow-lg"
        >
          {alerts.length === 0 ? (
            <p className="px-3 py-4 text-center text-sm text-input-text">
              Aucune alerte non lue
            </p>
          ) : (
            alerts.map((alert) => (
              <button
                key={alert.id}
                type="button"
                role="menuitem"
                onClick={() => handleAlertClick(alert.id)}
                className="flex w-full items-start gap-2 px-3 py-2 text-left text-sm hover:bg-background-secondary"
              >
                <span className={`mt-0.5 shrink-0 rounded-full px-2 py-0.5 text-[10px] font-medium ${KIND_STYLES[alert.kind]}`}>
                  {alert.kind === "expired" ? "périmé" : "alerte"}
                </span>
                <span className="text-foreground">{alert.message}</span>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  );
}

function BellIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" />
      <path d="M13.73 21a2 2 0 0 1-3.46 0" />
    </svg>
  );
}