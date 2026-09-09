import type { ButtonHTMLAttributes } from "react";

type ButtonVariant = "primary" | "alert" | "expired";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary:
    "bg-brand text-white hover:bg-brand-hover active:bg-black disabled:opacity-50 disabled:hover:bg-brand",
  alert:
    "bg-action-alert-bg text-white hover:bg-action-alert-bg-hover active:bg-black disabled:opacity-50 disabled:hover:bg-action-alert-bg",
  expired:
    "bg-action-expired-bg text-white hover:bg-action-expired-bg-hover active:bg-black disabled:opacity-50 disabled:hover:bg-action-expired-bg",
};

export function Button({
  variant = "primary",
  className = "",
  ...props
}: ButtonProps) {
  return (
    <button
      className={`inline-flex h-8 items-center justify-center rounded-2xl px-5 text-sm font-normal transition ${VARIANT_CLASSES[variant]} ${className}`}
      {...props}
    />
  );
}