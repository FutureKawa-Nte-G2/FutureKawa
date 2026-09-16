"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
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
      {/* useSearchParams (used to restore the country/warehouse/quality
          filter from the URL) opts this tree out of static rendering unless
          wrapped in Suspense — see the Next.js docs for that hook. */}
      <Suspense fallback={null}>
        <FifoContent />
      </Suspense>
    </AuthGate>
  );
}

const COUNTRY_PARAM = "country";
const WAREHOUSE_PARAM = "warehouse";
const QUALITY_PARAM = "quality";

function selectionToQueryString(selection: LocationSelection): string {
  const params = new URLSearchParams();
  if (selection.country) params.set(COUNTRY_PARAM, selection.country.code);
  if (selection.warehouse) params.set(WAREHOUSE_PARAM, selection.warehouse.id);
  if (selection.qualityGrade) params.set(QUALITY_PARAM, selection.qualityGrade);
  const query = params.toString();
  return query ? `?${query}` : "";
}

function FifoContent() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  // Read once on mount to seed LocationFilter's restoration — see that
  // component for why later changes to these values are ignored.
  const [initialCountryCode] = useState(() => searchParams.get(COUNTRY_PARAM));
  const [initialWarehouseId] = useState(() => searchParams.get(WAREHOUSE_PARAM));
  const [initialQualityGrade] = useState(() => searchParams.get(QUALITY_PARAM));

  const [selection, setSelection] = useState<LocationSelection>({
    country: null,
    warehouse: null,
    qualityGrade: null,
  });
  const [batches, setBatches] = useState<Batch[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { registerRefreshHandler } = useRefresh();
  const { accessToken } = useAuth();

  // While a country was carried in via the URL but LocationFilter hasn't
  // resolved it into a full Country object yet, hold off fetching — otherwise
  // we'd briefly fetch the unfiltered list before immediately refetching with
  // the restored filter applied.
  const isRestoringSelection = Boolean(initialCountryCode) && !selection.country;

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
    if (isRestoringSelection) return;
    // fetchBatches is async; its setState calls (setIsLoading/setError/setBatches/
    // setTotalPages) all happen after an await inside getBatches(), never
    // synchronously in this effect body. This is the standard data-fetching-on-mount
    // pattern documented by React itself. react-hooks/set-state-in-effect flags it
    // anyway because it can't trace setState timing through an awaited call —
    // known false positive here.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    fetchBatches();
  }, [fetchBatches, isRestoringSelection]);

  useEffect(() => {
    registerRefreshHandler(fetchBatches);
    return () => registerRefreshHandler(null);
  }, [registerRefreshHandler, fetchBatches]);

  function handleSelectionChange(next: LocationSelection) {
    setSelection(next);
    setPage(1);
    // Keep the filter in the URL so it survives navigating away (e.g. to a
    // batch's measurement detail page) and back — replace, not push, so
    // picking through country/warehouse/quality doesn't pile up history
    // entries the user would have to click "back" through repeatedly.
    router.replace(`${pathname}${selectionToQueryString(next)}`, { scroll: false });
  }

  function handlePageSizeChange(next: number) {
    setPageSize(next);
    setPage(1);
  }

  const title = selection.warehouse
    ? `${selection.country?.name} — ${selection.warehouse.name}`
    : selection.country?.name ?? "Tous les pays";

  // Quality grade is filtered client-side, on the page of batches already
  // fetched — GET /api/batches has no qualityGrade parameter (#74). This
  // means the filter only covers the current page, not the full paginated
  // list; see the issue's attention points.
  const visibleBatches = selection.qualityGrade
    ? batches.filter((batch) => batch.qualityGrade === selection.qualityGrade)
    : batches;

  return (
    <main className="flex-1 p-8">
      <LocationFilter
        onSelectionChange={handleSelectionChange}
        initialCountryCode={initialCountryCode}
        initialWarehouseId={initialWarehouseId}
        initialQualityGrade={initialQualityGrade}
      />
      <h1 className="text-2xl font-semibold text-foreground">{title}</h1>
      <p className="mt-1 text-sm text-input-text">
        Suivi des stocks et accès aux courbes de mesures qualité.
      </p>
      <div className="mt-8">
        {error ? (
          <p role="alert" className="py-8 text-center text-sm text-red-600">
            {error}
          </p>
        ) : isLoading || isRestoringSelection ? (
          <p className="py-8 text-center text-sm text-input-text">Chargement des lots...</p>
        ) : (
          <BatchTable batches={visibleBatches} />
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
