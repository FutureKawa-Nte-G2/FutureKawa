import type { Alert } from "@/lib/api/types";
import { AlertRow } from "./AlertRow";
import { GRID_TEMPLATE, HEADER_ROW_CLASSES } from "./grid";

interface AlertTableProps {
  alerts: Alert[];
}

const COLUMN_LABELS = [
  "Entrepôt",
  "Date alerte",
  "Date résolution",
  "Statut",
  "Type d'alerte",
  "Lots concernés",
  "Actions",
];

export function AlertTable({ alerts }: AlertTableProps) {
  if (alerts.length === 0) {
    return <p className="text-center text-sm text-input-text">Aucune alerte pour cette sélection.</p>;
  }

  return (
    <div className="flex w-full max-w-[990px] flex-col gap-3">
      <div className={`${HEADER_ROW_CLASSES} ${GRID_TEMPLATE} text-xs font-medium text-input-text`}>
        {COLUMN_LABELS.map((label) => (
          <span key={label} className="min-w-0 text-center">
            {label}
          </span>
        ))}
      </div>
      {alerts.map((alert) => (
        <AlertRow key={alert.id} alert={alert} />
      ))}
    </div>
  );
}
