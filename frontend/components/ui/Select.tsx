interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps {
  label: string;
  value: string;
  options: SelectOption[];
  placeholder?: string; // shown only while options is empty (loading/dependent state)
  disabled?: boolean;
  onChange: (value: string) => void;
}

export function Select({ label, value, options, placeholder, disabled, onChange }: SelectProps) {
  const hasOptions = options.length > 0;

  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-medium text-input-text">{label}</span>
      <select
        className="rounded-2xl border border-border-primary bg-white px-3 py-2 font-input text-sm text-input-text disabled:cursor-not-allowed disabled:opacity-50"
        value={hasOptions ? value : ""}
        disabled={disabled || !hasOptions}
        onChange={(event) => onChange(event.target.value)}
      >
        {!hasOptions && (
          <option value="" disabled>
            {placeholder}
          </option>
        )}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}