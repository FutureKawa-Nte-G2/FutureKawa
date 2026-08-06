"use client";

import { useEffect, useState } from "react";
import { LocationFilter, type LocationSelection } from "@/components/batches/LocationFilter";
import { BatchTable } from "@/components/batches/BatchTable";
import { PageSizeSelector } from "@/components/ui/PageSizeSelector";
import { Pagination } from "@/components/ui/Pagination";
import { getBatches } from "@/lib/api/batches";
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

  useEffect(() => {
    getBatches({
      countryCode: selection.country?.code,
      warehouseId: selection.warehouse?.id,
      page,
      pageSize,
    }).then((response) => {
      setBatches(response.batches);
      setTotalPages(response.totalPages);
    });
  }, [selection, page, pageSize]);

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