"use client";

import { useEffect, useState } from "react";
import { getCountries, getWarehouses } from "@/lib/api/batches";
import type { Country, Warehouse } from "@/lib/api/types";
import { Select } from "@/components/ui/Select";

export interface LocationSelection {
  countryCode: string | null;
  warehouseId: string | null;
}

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

  function handleCountryChange(nextCountryCode: string) {
    setCountryCode(nextCountryCode);
    setWarehouseId(null);
    onSelectionChange({ countryCode: nextCountryCode, warehouseId: null });
  }

  function handleWarehouseChange(nextWarehouseId: string) {
    setWarehouseId(nextWarehouseId);
    onSelectionChange({ countryCode, warehouseId: nextWarehouseId });
  }

  return (
    <aside className="flex h-full w-[224px] flex-col gap-4 bg-border-secondary p-4">
      <Select
        label="Country"
        value={countryCode}
        options={countries.map((country) => ({ value: country.code, label: country.name }))}
        placeholder="Select a country"
        onChange={handleCountryChange}
      />
      <Select
        label="Warehouse"
        value={warehouseId}
        options={warehouses.map((warehouse) => ({ value: warehouse.id, label: warehouse.name }))}
        placeholder={countryCode ? "Select a warehouse" : "Select a country first"}
        disabled={!countryCode}
        onChange={handleWarehouseChange}
      />
    </aside>
  );
}