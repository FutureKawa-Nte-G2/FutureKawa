"use client";

import { useEffect, useState } from "react";
import { getCountries, getWarehouses } from "@/lib/api/batches";
import type { Country, Warehouse } from "@/lib/api/types";
import { Select } from "@/components/ui/Select";
import { useAuth } from "@/context/AuthContext";

export interface LocationSelection {
  country: Country | null;
  warehouse: Warehouse | null;
}

const ALL_COUNTRIES = "ALL_COUNTRIES";
const ALL_WAREHOUSES = "ALL_WAREHOUSES";

interface LocationFilterProps {
  onSelectionChange: (selection: LocationSelection) => void;
}

export function LocationFilter({ onSelectionChange }: LocationFilterProps) {
  const [countries, setCountries] = useState<Country[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [countryCode, setCountryCode] = useState<string | null>(null);
  const [warehouseId, setWarehouseId] = useState<string | null>(null);
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
      onSelectionChange({ country: null, warehouse: null });
      return;
    }
    const country = countries.find((c) => c.code === next) ?? null;
    setCountryCode(next);
    setWarehouseId(null);
    onSelectionChange({ country, warehouse: null });
  }

  function handleWarehouseChange(next: string) {
    const country = countries.find((c) => c.code === countryCode) ?? null;
    if (next === ALL_WAREHOUSES) {
      setWarehouseId(null);
      onSelectionChange({ country, warehouse: null });
      return;
    }
    const warehouse = warehouses.find((w) => w.id === next) ?? null;
    setWarehouseId(next);
    onSelectionChange({ country, warehouse });
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

  return (
    <aside className="flex w-(--sidebar-width) flex-col gap-4 bg-background-secondary p-4">
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
    </aside>
  );
}