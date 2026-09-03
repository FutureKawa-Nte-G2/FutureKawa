import type { BatchStatus } from "@/lib/api/types";
import { Button } from "@/components/ui/Button";

type ButtonVariant = "primary" | "alert" | "expired";

const STATUS_TO_VARIANT: Record<BatchStatus, ButtonVariant> = {
  compliant: "primary",
  expired: "expired",
};

interface QualityTrackingButtonProps {
  status: BatchStatus;
  onClick?: () => void;
}

export function QualityTrackingButton({ status, onClick }: QualityTrackingButtonProps) {
  return (
    <Button variant={STATUS_TO_VARIANT[status]} onClick={onClick}>
      Suivi qualité
    </Button>
  );
}