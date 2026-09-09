"use client";

import { useEffect, useState } from "react";
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
}

export function LocationFilter({ onSelectionChange }: LocationFilterProps) {
  const [countries, setCountries] = useState<Country[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [countryCode, setCountryCode] = useState<string | null>(null);
  const [warehouseId, setWarehouseId] = useState<string | null>(null);
  const [qualityGrade, setQualityGrade] = useState<string | null>(null);
  const { accessToken } = useAuth();

  useEffect(() => {
    getCountries(accessToken).then(setCountries);
  }, [accessToken]);

  // Fetch warehouses only when a country is actually selected.
  // Clearing warehouses when no country is selected is handled directly
  // in handleCountryChange (a user event), not here.
  useEffect(() => {
    if (!countryCode) return;
    getWarehouses(countryCode, accessToken).then(setWarehouses);
  }, [countryCode, accessToken]);

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

  // Horizontal filter bar, displayed at the top of the FIFO page (was
  // previously a vertical <aside> occupying the page's left-hand sidebar
  // column — see #74).
  return (
    <div className="flex flex-wrap items-end gap-4 pb-6">
      <Select
        label="Pays"
        value={countryCode ?? ALL_COUNTRIES}
        options={countryOptions}
        onChange={handleCountryChange}
      />
      <Select
        label="Entrepôt"
        value={warehouseId ?? ALL_WAREHOUSES}
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
