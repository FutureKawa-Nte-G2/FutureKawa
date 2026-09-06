"use client";

import { useCallback, useEffect, useState } from "react";
import { LocationFilter, type LocationSelection } from "@/components/batches/LocationFilter";
import { BatchTable } from "@/components/batches/BatchTable";
import { PageSizeSelector } from "@/components/ui/PageSizeSelector";
import { Pagination } from "@/components/ui/Pagination";
import { getBatches } from "@/lib/api/batches";
import { useRefresh } from "@/context/RefreshContext";
import { useAuth } from "@/context/AuthContext";
import type { Batch } from "@/lib/api/types";

export default function FifoPage() {
  const [selection, setSelection] = useState<LocationSelection>({
    country: null,
    warehouse: null,
  });
  const [batches, setBatches] = useState<Batch[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const { registerRefreshHandler } = useRefresh();
  const { accessToken } = useAuth();

  const fetchBatches = useCallback(async () => {
    const response = await getBatches({
      countryCode: selection.country?.code,
      warehouseId: selection.warehouse?.id,
      page,
      pageSize,
      accessToken,
    });
    setBatches(response.batches);
    setTotalPages(response.totalPages);
  }, [selection, page, pageSize, accessToken]);

  useEffect(() => {
    // fetchBatches is async; its setState calls happen after the await
    // inside getBatches(), never synchronously in this effect body. This is
    // the standard data-fetching-on-mount pattern documented by React itself.
    // react-hooks/set-state-in-effect flags it anyway because it can't trace
    // setState timing through an awaited call — known false positive here.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchBatches();
  }, [fetchBatches]);

  // Declare this page as the current handler for the navbar's refresh button.
  // Cleared on unmount so a stale page doesn't respond after navigating away.
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
          <BatchTable batches={batches} />
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