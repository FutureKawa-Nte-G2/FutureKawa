"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { AuthGate } from "@/components/auth/AuthGate";
import { Badge } from "@/components/ui/Badge";
import { MeasurementsCharts } from "@/components/batches/MeasurementsCharts";
import { getBatchById } from "@/lib/api/batches";
import { filterMeasurementsByStoragePeriod, getWarehouseMeasurements } from "@/lib/api/measurements";
import { useAuth } from "@/context/AuthContext";
import type { Batch, Measurement } from "@/lib/api/types";

// Issue #35 restricts this page to Quality Agent / Quality Manager, but the
// role gate was dropped for the PoC (09/09, Laurent) — it complicated the
// demo and isn't needed yet; any authenticated user (the seeded admin test
// account) can view the page for now.
export default function BatchDetailPage() {
  return (
    <AuthGate>
      <BatchDetailContent />
    </AuthGate>
  );
}

function BatchDetailContent() {
  const params = useParams<{ countryCode: string; id: string }>();
  const router = useRouter();
  const { accessToken } = useAuth();
  const [batch, setBatch] = useState<Batch | null>(null);
  const [measurements, setMeasurements] = useState<Measurement[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const batchData = await getBatchById(params.id, accessToken);
      // The warehouse's daily aggregates (GET /api/measurements/{warehouseId})
      // are scoped to the warehouse, not the batch (see
      // backlog-frontend-mesures-alertes-collecte.md) — narrow to this batch's
      // storage window client-side.
      const warehouseMeasurements = await getWarehouseMeasurements(batchData.warehouseId, accessToken);
      setBatch(batchData);
      setMeasurements(filterMeasurementsByStoragePeriod(warehouseMeasurements, batchData));
    } catch {
      setError("Impossible de charger les données de ce lot. Réessayez dans un instant.");
    } finally {
      setIsLoading(false);
    }
  }, [params.id, accessToken]);

  useEffect(() => {
    // Same data-fetching-on-mount pattern as FifoContent/AlertesContent — see
    // the eslint-disable comment there for why react-hooks/set-state-in-effect
    // is a known false positive here.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchData();
  }, [fetchData]);

  return (
    <main className="flex-1 p-8">
      <button
        type="button"
        onClick={() => router.push("/fifo")}
        className="mb-4 text-sm text-input-text hover:text-foreground"
      >
        ← Retour au FIFO
      </button>

      {error ? (
        <p role="alert" className="py-8 text-center text-sm text-red-600">
          {error}
        </p>
      ) : isLoading || !batch ? (
        <p className="py-8 text-center text-sm text-input-text">Chargement du lot...</p>
      ) : (
        <>
          <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
            <div>
              <h1 className="text-2xl font-semibold text-foreground">{batch.batchRef}</h1>
              <p className="mt-1 text-sm text-input-text">
                {batch.farmName} — {batch.warehouseName} ({batch.countryName})
              </p>
            </div>
            <Badge status={batch.status} />
          </div>

          <dl className="mb-8 grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
            <div>
              <dt className="text-input-text">Qualité</dt>
              <dd className="font-medium text-foreground">{batch.qualityGrade}</dd>
            </div>
            <div>
              <dt className="text-input-text">Entré en stock</dt>
              <dd className="font-medium text-foreground">{formatDate(batch.enteredAt)}</dd>
            </div>
            <div>
              <dt className="text-input-text">Expédié</dt>
              <dd className="font-medium text-foreground">
                {batch.shippedAt ? formatDate(batch.shippedAt) : "En stock"}
              </dd>
            </div>
          </dl>

          <h2 className="mb-1 text-lg font-semibold text-foreground">Relevés</h2>
          <p className="mb-4 text-sm text-input-text">
            Température et humidité de l&apos;entrepôt pendant la période de stockage du lot.
          </p>
          <MeasurementsCharts measurements={measurements} />
        </>
      )}
    </main>
  );
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("fr-FR", { year: "numeric", month: "short", day: "numeric" });
}
