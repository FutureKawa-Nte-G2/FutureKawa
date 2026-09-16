export function SearchInput() {
  return (
    <div className="relative flex w-full items-center">
      <SearchIcon />
      <input
        type="text"
        placeholder="Recherche"
        disabled
        aria-label="Recherche"
        className="w-full rounded-lg border border-border-primary bg-background py-2 pl-9 pr-14 text-sm text-foreground placeholder:text-input-text disabled:cursor-not-allowed disabled:opacity-70"
      />
      <kbd className="pointer-events-none absolute right-2 flex items-center gap-0.5 rounded border border-border-primary px-1.5 py-0.5 text-[10px] font-medium text-input-text">
        ⌘ F
      </kbd>
    </div>
  );
}

function SearchIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      className="pointer-events-none absolute left-3 text-input-text"
    >
      <circle cx="11" cy="11" r="8" />
      <path d="m21 21-4.3-4.3" />
    </svg>
  );
}