"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { getUnreadAlerts, subscribeToAlertsChange } from "@/lib/api/alerts";
import type { Alert, AlertType } from "@/lib/api/types";
import { useAuth } from "@/context/AuthContext";

const TYPE_STYLES: Record<AlertType, string> = {
  temperature: "text-status-alert-text bg-status-alert-bg",
  humidity: "text-status-alert-text bg-status-alert-bg",
};

const TYPE_LABELS: Record<AlertType, string> = {
  temperature: "Température",
  humidity: "Humidité",
};

export function AlertButton() {
  const [alerts, setAlerts] = useState<Alert[]>([]);
  const [isOpen, setIsOpen] = useState(false);
  const { accessToken } = useAuth();
  const router = useRouter();

  useEffect(() => {
    let cancelled = false;

    function fetchUnreadAlerts() {
      getUnreadAlerts(accessToken)
        .then((result) => {
          if (!cancelled) setAlerts(result);
        })
        .catch((error) => {
          if (!cancelled) {
            console.error("Impossible de récupérer les alertes non lues :", error);
          }
        });
    }

    fetchUnreadAlerts();
    const unsubscribe = subscribeToAlertsChange(fetchUnreadAlerts);

    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, [accessToken]);

  function handleAlertClick() {
    setIsOpen(false);
    router.push("/alertes");
  }

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-label={`Alertes non lues (${alerts.length})`}
        title={`Alertes non lues (${alerts.length})`}
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
                onClick={handleAlertClick}
                className="flex w-full items-start gap-2 px-3 py-2 text-left text-sm hover:bg-background-secondary"
              >
                <span className={`mt-0.5 shrink-0 rounded-full px-2 py-0.5 text-[10px] font-medium ${TYPE_STYLES[alert.type]}`}>
                  {TYPE_LABELS[alert.type]}
                </span>
                <span className="text-foreground">
                  Entrepôt {alert.warehouseName} ({alert.countryName})
                </span>
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
