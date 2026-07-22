import type { Batch } from "@/lib/api/types";
import { BatchRow } from "./BatchRow";
import { GRID_TEMPLATE, HEADER_ROW_CLASSES } from "./grid";

interface BatchTableProps {
  batches: Batch[];
}

const COLUMN_LABELS = ["Id lot", "Exploitation", "Entrepôt", "Date de stockage", "Statut", "Actions"];

export function BatchTable({ batches }: BatchTableProps) {
  if (batches.length === 0) {
    return <p className="text-center text-sm text-input-text">Aucun lot en stock pour cette sélection.</p>;
  }

  return (
    <div className="flex w-full max-w-[990px] flex-col gap-3">
      <div className={`${HEADER_ROW_CLASSES} ${GRID_TEMPLATE} text-xs font-medium text-input-text`}>
        {COLUMN_LABELS.map((label) => (
          <span key={label}>{label}</span>
        ))}
      </div>
      {batches.map((batch) => (
        <BatchRow key={batch.id} batch={batch} />
      ))}
    </div>
  );
}