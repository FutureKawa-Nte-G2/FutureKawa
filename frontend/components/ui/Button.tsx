import type { ButtonHTMLAttributes } from "react";

type ButtonVariant = "primary";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary:
    "bg-brand text-white hover:bg-brand-hover active:bg-black disabled:opacity-50 disabled:hover:bg-brand",
};

export function Button({
  variant = "primary",
  className = "",
  ...props
}: ButtonProps) {
  return (
    <button
      className={`rounded-2xl px-5 py-2.5 text-sm font-normal transition ${VARIANT_CLASSES[variant]} ${className}`}
      {...props}
    />
  );
}