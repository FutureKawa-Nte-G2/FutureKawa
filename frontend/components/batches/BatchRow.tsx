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

function formatEnteredAt(iso: string): string {
  return new Date(iso).toLocaleDateString("fr-FR", { year: "numeric", month: "short", day: "numeric" });
}