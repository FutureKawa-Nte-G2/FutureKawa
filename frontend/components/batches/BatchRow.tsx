"use client";

import { useRouter } from "next/navigation";
import type { Batch } from "@/lib/api/types";
import { Badge } from "@/components/ui/Badge";
import { QualityTrackingButton } from "./QualityTrackingButton";
import { GRID_TEMPLATE, ROW_CLASSES } from "./grid";

interface BatchRowProps {
  batch: Batch;
}

export function BatchRow({ batch }: BatchRowProps) {
  const router = useRouter();

  return (
    <div className={`${ROW_CLASSES} ${GRID_TEMPLATE}`}>
      <span className="min-w-0 text-center text-sm text-foreground">{batch.batchRef}</span>
      <span className="min-w-0 text-center text-sm text-foreground">{batch.farmName}</span>
      <span className="min-w-0 text-center text-sm text-foreground">{batch.warehouseName}</span>
      <span className="min-w-0 text-center text-sm text-foreground">{batch.qualityGrade}</span>
      <span className="min-w-0 text-center text-sm text-foreground">{formatEnteredAt(batch.enteredAt)}</span>
      <Badge status={batch.status} />
      <QualityTrackingButton
        status={batch.status}
        onClick={() => router.push(`/batches/${batch.countryCode}/${batch.id}`)}
      />
    </div>
  );
}

// Anchored at local midnight on the UTC calendar date (the yyyy-MM-dd
// prefix), not converted from the full UTC instant — otherwise a late-UTC
// time of day can roll over to the next day once rendered in a timezone
// ahead of UTC. See the batch detail page's formatDate for the same fix.
function formatEnteredAt(iso: string): string {
  return new Date(`${iso.slice(0, 10)}T00:00:00`).toLocaleDateString("fr-FR", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}