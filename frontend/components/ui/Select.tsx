interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps {
  label: string;
  value: string | null;
  options: SelectOption[];
  placeholder: string;
  disabled?: boolean;
  onChange: (value: string) => void;
}

export function Select({ label, value, options, placeholder, disabled, onChange }: SelectProps) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-medium text-input-text">{label}</span>
      <select
        className="rounded-2xl border border-border-primary bg-white px-3 py-2 font-input text-sm text-input-text disabled:cursor-not-allowed disabled:opacity-50"
        value={value ?? ""}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="" disabled>
          {placeholder}
        </option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}