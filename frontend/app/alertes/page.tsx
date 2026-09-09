"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertFilter, type AlertFilterSelection } from "@/components/alerts/AlertFilter";
import { AlertTable } from "@/components/alerts/AlertTable";
import { PageSizeSelector } from "@/components/ui/PageSizeSelector";
import { Pagination } from "@/components/ui/Pagination";
import { getAlerts } from "@/lib/api/alerts";
import { useRefresh } from "@/context/RefreshContext";
import { useAuth } from "@/context/AuthContext";
import { AuthGate } from "@/components/auth/AuthGate";
import type { Alert } from "@/lib/api/types";

export default function AlertesPage() {
  return (
    <AuthGate>
      <AlertesContent />
    </AuthGate>
  );
}

function AlertesContent() {
  const [selection, setSelection] = useState<AlertFilterSelection>({
    country: null,
    warehouse: null,
    status: null,
  });
  const [alerts, setAlerts] = useState<Alert[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { registerRefreshHandler } = useRefresh();
  const { accessToken } = useAuth();

  const fetchAlerts = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await getAlerts({
        countryCode: selection.country?.code,
        warehouseId: selection.warehouse?.id,
        status: selection.status ?? undefined,
        page,
        pageSize,
        accessToken,
      });
      setAlerts(response.alerts);
      setTotalPages(response.totalPages);
    } catch {
      setError("Impossible de charger les alertes. Réessayez dans un instant.");
    } finally {
      setIsLoading(false);
    }
  }, [selection, page, pageSize, accessToken]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchAlerts();
  }, [fetchAlerts]);

  useEffect(() => {
    registerRefreshHandler(fetchAlerts);
    return () => registerRefreshHandler(null);
  }, [registerRefreshHandler, fetchAlerts]);

  function handleSelectionChange(next: AlertFilterSelection) {
    setSelection(next);
    setPage(1);
  }

  function handlePageSizeChange(next: number) {
    setPageSize(next);
    setPage(1);
  }

  return (
    <main className="flex-1 p-8">
      <AlertFilter onSelectionChange={handleSelectionChange} />
      <h1 className="text-2xl font-semibold text-foreground">Alertes</h1>
      <p className="mt-1 text-sm text-input-text">
        Suivi des dépassements de seuil température/humidité par entrepôt.
      </p>
      <div className="mt-8">
        {error ? (
          <p role="alert" className="py-8 text-center text-sm text-red-600">
            {error}
          </p>
        ) : isLoading ? (
          <p className="py-8 text-center text-sm text-input-text">Chargement des alertes...</p>
        ) : (
          <AlertTable alerts={alerts} onAlertResolved={fetchAlerts} />
        )}
      </div>
      <div className="mt-6 grid grid-cols-3 items-center">
        <div className="justify-self-start">
          <PageSizeSelector value={pageSize} onChange={handlePageSizeChange} />
        </div>
        <div className="justify-self-center">
          <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
        </div>
      </div>
    </main>
  );
}
