"use client";

import { useEffect, useState } from "react";
import { getCountries, getWarehouses } from "@/lib/api/batches";
import type { AlertStatus, Country, Warehouse } from "@/lib/api/types";
import { Select } from "@/components/ui/Select";
import { useAuth } from "@/context/AuthContext";

export interface AlertFilterSelection {
  country: Country | null;
  warehouse: Warehouse | null;
  status: AlertStatus | null;
}

const ALL_COUNTRIES = "ALL_COUNTRIES";
const ALL_WAREHOUSES = "ALL_WAREHOUSES";
const ALL_STATUSES = "ALL_STATUSES";

const STATUS_OPTIONS = [
  { value: ALL_STATUSES, label: "Tous les statuts" },
  { value: "active", label: "Active" },
  { value: "resolved", label: "Résolue" },
];

interface AlertFilterProps {
  onSelectionChange: (selection: AlertFilterSelection) => void;
}

export function AlertFilter({ onSelectionChange }: AlertFilterProps) {
  const [countries, setCountries] = useState<Country[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [countryCode, setCountryCode] = useState<string | null>(null);
  const [warehouseId, setWarehouseId] = useState<string | null>(null);
  const [status, setStatus] = useState<AlertStatus | null>(null);
  const { accessToken } = useAuth();

  useEffect(() => {
    getCountries(accessToken).then(setCountries);
  }, [accessToken]);

  useEffect(() => {
    if (!countryCode) return;
    getWarehouses(countryCode, accessToken).then(setWarehouses);
  }, [countryCode, accessToken]);

  function handleCountryChange(next: string) {
    if (next === ALL_COUNTRIES) {
      setCountryCode(null);
      setWarehouseId(null);
      setWarehouses([]);
      onSelectionChange({ country: null, warehouse: null, status });
      return;
    }
    const country = countries.find((c) => c.code === next) ?? null;
    setCountryCode(next);
    setWarehouseId(null);
    onSelectionChange({ country, warehouse: null, status });
  }

  function handleWarehouseChange(next: string) {
    const country = countries.find((c) => c.code === countryCode) ?? null;
    if (next === ALL_WAREHOUSES) {
      setWarehouseId(null);
      onSelectionChange({ country, warehouse: null, status });
      return;
    }
    const warehouse = warehouses.find((w) => w.id === next) ?? null;
    setWarehouseId(next);
    onSelectionChange({ country, warehouse, status });
  }

  function handleStatusChange(next: string) {
    const nextStatus = next === ALL_STATUSES ? null : (next as AlertStatus);
    const country = countries.find((c) => c.code === countryCode) ?? null;
    const warehouse = warehouses.find((w) => w.id === warehouseId) ?? null;
    setStatus(nextStatus);
    onSelectionChange({ country, warehouse, status: nextStatus });
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
        label="Statut"
        value={status ?? ALL_STATUSES}
        options={STATUS_OPTIONS}
        onChange={handleStatusChange}
      />
    </div>
  );
}
