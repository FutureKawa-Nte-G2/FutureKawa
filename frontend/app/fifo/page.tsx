"use client";

import { useCallback, useEffect, useState } from "react";
import { LocationFilter, type LocationSelection } from "@/components/batches/LocationFilter";
import { BatchTable } from "@/components/batches/BatchTable";
import { PageSizeSelector } from "@/components/ui/PageSizeSelector";
import { Pagination } from "@/components/ui/Pagination";
import { getBatches } from "@/lib/api/batches";
import { useRefresh } from "@/context/RefreshContext";
import { useAuth } from "@/context/AuthContext";
import { AuthGate } from "@/components/auth/AuthGate";
import type { Batch } from "@/lib/api/types";

// AuthGate only mounts FifoContent once a valid session is confirmed
export default function FifoPage() {
  return (
    <AuthGate>
      <FifoContent />
    </AuthGate>
  );
}

function FifoContent() {
  const [selection, setSelection] = useState<LocationSelection>({
    country: null,
    warehouse: null,
  });
  const [batches, setBatches] = useState<Batch[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { registerRefreshHandler } = useRefresh();
  const { accessToken } = useAuth();

  const fetchBatches = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await getBatches({
        countryCode: selection.country?.code,
        warehouseId: selection.warehouse?.id,
        page,
        pageSize,
        accessToken,
      });
      setBatches(response.batches);
      setTotalPages(response.totalPages);
    } catch {
      setError("Impossible de charger les lots. Réessayez dans un instant.");
    } finally {
      setIsLoading(false);
    }
  }, [selection, page, pageSize, accessToken]);

  useEffect(() => {
    fetchBatches();
  }, [fetchBatches]);

  useEffect(() => {
    registerRefreshHandler(fetchBatches);
    return () => registerRefreshHandler(null);
  }, [registerRefreshHandler, fetchBatches]);

  function handleSelectionChange(next: LocationSelection) {
    setSelection(next);
    setPage(1);
  }

  function handlePageSizeChange(next: number) {
    setPageSize(next);
    setPage(1);
  }

  const title = selection.warehouse
    ? `${selection.country?.name} — ${selection.warehouse.name}`
    : selection.country?.name ?? "Tous les pays";

  return (
    <div className="flex min-h-screen">
      <LocationFilter onSelectionChange={handleSelectionChange} />
      <main className="flex-1 p-8">
        <h1 className="text-2xl font-semibold text-foreground">{title}</h1>
        <p className="mt-1 text-sm text-input-text">
          Suivi des stocks et accès aux courbes de mesures qualité.
        </p>
        <div className="mt-8">
          {error ? (
            <p role="alert" className="py-8 text-center text-sm text-red-600">
              {error}
            </p>
          ) : isLoading ? (
            <p className="py-8 text-center text-sm text-input-text">Chargement des lots...</p>
          ) : (
            <BatchTable batches={batches} />
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
    </div>
  );
}