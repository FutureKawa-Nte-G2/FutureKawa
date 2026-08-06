const PAGE_SIZE_OPTIONS = [10, 15, 20] as const;

interface PageSizeSelectorProps {
  value: number;
  onChange: (pageSize: number) => void;
}

export function PageSizeSelector({ value, onChange }: PageSizeSelectorProps) {
  return (
    <label className="flex items-center gap-2 text-sm text-input-text">
      Afficher
      <select
        className="rounded-2xl border border-border-primary bg-white px-3 py-1.5 font-input text-sm text-input-text"
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      >
        {PAGE_SIZE_OPTIONS.map((size) => (
          <option key={size} value={size}>
            {size}
          </option>
        ))}
      </select>
      Lignes
    </label>
  );
}