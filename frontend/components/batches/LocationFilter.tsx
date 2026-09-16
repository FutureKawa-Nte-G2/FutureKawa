"use client";

import { useEffect, useRef, useState } from "react";
import { getCountries, getWarehouses } from "@/lib/api/batches";
import type { Country, Warehouse } from "@/lib/api/types";
import { Select } from "@/components/ui/Select";
import { useAuth } from "@/context/AuthContext";

export interface LocationSelection {
  country: Country | null;
  warehouse: Warehouse | null;
  qualityGrade: string | null;
}

const ALL_COUNTRIES = "ALL_COUNTRIES";
const ALL_WAREHOUSES = "ALL_WAREHOUSES";
const ALL_QUALITY_GRADES = "ALL_QUALITY_GRADES";

// Fixed set mirroring the backend BatchQualityGrade enum (A/B/C). Unlike
// countries/warehouses this isn't fetched from the API — it's a small,
// stable enum, not reference data.
const QUALITY_GRADE_OPTIONS = [
  { value: ALL_QUALITY_GRADES, label: "Toutes les qualités" },
  { value: "A", label: "A" },
  { value: "B", label: "B" },
  { value: "C", label: "C" },
];

interface LocationFilterProps {
  onSelectionChange: (selection: LocationSelection) => void;
  // Lets a parent restore a previously-made selection (e.g. persisted in the
  // URL) once the reference data needed to resolve it into full Country/
  // Warehouse objects has loaded. Only read on mount — later changes to
  // these props are ignored, matching how React initial state works.
  initialCountryCode?: string | null;
  initialWarehouseId?: string | null;
  initialQualityGrade?: string | null;
}

export function LocationFilter({
  onSelectionChange,
  initialCountryCode = null,
  initialWarehouseId = null,
  initialQualityGrade = null,
}: LocationFilterProps) {
  const [countries, setCountries] = useState<Country[]>([]);
  const [countriesLoaded, setCountriesLoaded] = useState(false);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  // Which countryCode the current `warehouses` list was fetched for — not a
  // plain boolean, so that "loaded" can be derived (`=== countryCode` below)
  // instead of needing a synchronous reset-to-false setState when countryCode
  // changes (that pattern trips react-hooks/set-state-in-effect).
  const [warehousesLoadedFor, setWarehousesLoadedFor] = useState<string | null>(null);
  const [countryCode, setCountryCode] = useState<string | null>(initialCountryCode);
  const [warehouseId, setWarehouseId] = useState<string | null>(initialWarehouseId);
  const [qualityGrade, setQualityGrade] = useState<string | null>(initialQualityGrade);
  const { accessToken } = useAuth();

  useEffect(() => {
    getCountries(accessToken).then((result) => {
      setCountries(result);
      setCountriesLoaded(true);
    });
  }, [accessToken]);

  // Fetch warehouses only when a country is actually selected.
  // Clearing warehouses when no country is selected is handled directly
  // in handleCountryChange (a user event), not here.
  useEffect(() => {
    if (!countryCode) return;
    getWarehouses(countryCode, accessToken).then((result) => {
      setWarehouses(result);
      setWarehousesLoadedFor(countryCode);
    });
  }, [countryCode, accessToken]);

  const warehousesLoaded = warehousesLoadedFor === countryCode;

  // Restores a selection carried in via initialCountryCode/initialWarehouseId
  // (the FIFO page seeds these from the URL, so a filter picked before
  // navigating to a batch's measurement detail is still there when the user
  // comes back — see issue "conserver les filtres FIFO"). The parent only
  // knows Country/Warehouse *objects*, not bare codes, so this waits for the
  // matching reference data to load before notifying it once. Runs at most
  // once: hasNotifiedRestore guards against re-firing (and re-triggering a
  // batch refetch) on every later countries/warehouses reload.
  const hasNotifiedRestore = useRef(false);
  useEffect(() => {
    if (hasNotifiedRestore.current) return;
    if (!countryCode) {
      hasNotifiedRestore.current = true;
      return;
    }
    if (!countriesLoaded) return;
    const country = countries.find((c) => c.code === countryCode) ?? null;
    if (!country) {
      // Unknown/stale country code — nothing to restore.
      hasNotifiedRestore.current = true;
      return;
    }
    if (!warehouseId) {
      hasNotifiedRestore.current = true;
      onSelectionChange({ country, warehouse: null, qualityGrade });
      return;
    }
    if (!warehousesLoaded) return;
    const warehouse = warehouses.find((w) => w.id === warehouseId) ?? null;
    hasNotifiedRestore.current = true;
    // A stale/unknown warehouse id simply resolves to warehouse: null here —
    // we don't also clear the warehouseId state (that would be a synchronous
    // setState in this effect, flagged by react-hooks/set-state-in-effect).
    // The Select below already falls back to "Tous les entrepôts" whenever
    // warehouseId doesn't match any loaded option, so nothing renders
    // incorrectly either way.
    onSelectionChange({ country, warehouse, qualityGrade });
  }, [countriesLoaded, countries, warehousesLoaded, warehouses, countryCode, warehouseId, qualityGrade, onSelectionChange]);

  function handleCountryChange(next: string) {
    if (next === ALL_COUNTRIES) {
      setCountryCode(null);
      setWarehouseId(null);
      setWarehouses([]);
      onSelectionChange({ country: null, warehouse: null, qualityGrade });
      return;
    }
    const country = countries.find((c) => c.code === next) ?? null;
    setCountryCode(next);
    setWarehouseId(null);
    onSelectionChange({ country, warehouse: null, qualityGrade });
  }

  function handleWarehouseChange(next: string) {
    const country = countries.find((c) => c.code === countryCode) ?? null;
    if (next === ALL_WAREHOUSES) {
      setWarehouseId(null);
      onSelectionChange({ country, warehouse: null, qualityGrade });
      return;
    }
    const warehouse = warehouses.find((w) => w.id === next) ?? null;
    setWarehouseId(next);
    onSelectionChange({ country, warehouse, qualityGrade });
  }

  // Quality is filtered client-side, on whichever page of batches is already
  // loaded — GET /api/batches has no qualityGrade parameter today (#74,
  // attention points). Adding it server-side, to filter the full paginated
  // list rather than just the current page, is tracked as a follow-up.
  function handleQualityChange(next: string) {
    const grade = next === ALL_QUALITY_GRADES ? null : next;
    const country = countries.find((c) => c.code === countryCode) ?? null;
    const warehouse = warehouses.find((w) => w.id === warehouseId) ?? null;
    setQualityGrade(grade);
    onSelectionChange({ country, warehouse, qualityGrade: grade });
  }

  const countryOptions = [
    { value: ALL_COUNTRIES, label: "Tous les pays" },
    ...countries.map((country) => ({ value: country.code, label: country.name })),
  ];

  const warehouseOptions = countryCode
    ? [
        { value: ALL_WAREHOUSES, label: "Tous les entrepôts" },
        ...warehouses.map((warehouse) => ({ value: warehouse.id, label: warehouse.name })),
      ]
    : [];

  // Fall back to the "all" option whenever the current code doesn't match
  // any loaded option — covers both "nothing selected" and a stale/unknown
  // code restored from the URL (see the restoration effect above).
  const countrySelectValue =
    countryCode && countryOptions.some((option) => option.value === countryCode) ? countryCode : ALL_COUNTRIES;
  const warehouseSelectValue =
    warehouseId && warehouseOptions.some((option) => option.value === warehouseId) ? warehouseId : ALL_WAREHOUSES;

  // Horizontal filter bar, displayed at the top of the FIFO page (was
  // previously a vertical <aside> occupying the page's left-hand sidebar
  // column — see #74).
  return (
    <div className="flex flex-wrap items-end gap-4 pb-6">
      <Select label="Pays" value={countrySelectValue} options={countryOptions} onChange={handleCountryChange} />
      <Select
        label="Entrepôt"
        value={warehouseSelectValue}
        options={warehouseOptions}
        placeholder="Sélectionnez d'abord un pays"
        disabled={!countryCode}
        onChange={handleWarehouseChange}
      />
      <Select
        label="Qualité"
        value={qualityGrade ?? ALL_QUALITY_GRADES}
        options={QUALITY_GRADE_OPTIONS}
        onChange={handleQualityChange}
      />
    </div>
  );
}
