"use client";

import { useEffect, useState } from "react";
import { getCountries, getWarehouses } from "@/lib/api/batches";
import type { Country, Warehouse } from "@/lib/api/types";
import { Select } from "@/components/ui/Select";

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

  useEffect(() => {
    getCountries().then(setCountries);
  }, []);

  useEffect(() => {
    if (!countryCode) {
      setWarehouses([]);
      return;
    }
    getWarehouses(countryCode).then(setWarehouses);
  }, [countryCode]);

  function handleCountryChange(next: string) {
    if (next === ALL_COUNTRIES) {
      setCountryCode(null);
      setWarehouseId(null);
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
    { value: ALL_COUNTRIES, label: "All countries" },
    ...countries.map((country) => ({ value: country.code, label: country.name })),
  ];

  const warehouseOptions = countryCode
    ? [
        { value: ALL_WAREHOUSES, label: "All warehouses" },
        ...warehouses.map((warehouse) => ({ value: warehouse.id, label: warehouse.name })),
      ]
    : [];

  return (
    <aside className="flex w-[224px] flex-col gap-4 bg-border-secondary p-4">
      <Select
        label="Country"
        value={countryCode ?? ALL_COUNTRIES}
        options={countryOptions}
        onChange={handleCountryChange}
      />
      <Select
        label="Warehouse"
        value={warehouseId ?? ALL_WAREHOUSES}
        options={warehouseOptions}
        placeholder="Select a country first"
        disabled={!countryCode}
        onChange={handleWarehouseChange}
      />
    </aside>
  );
}