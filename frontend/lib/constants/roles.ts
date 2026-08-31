// Displayed labels are in French; role values themselves stay as the API contract (English identifiers)
const ROLE_LABELS: Record<string, string> = {
  CEO: "Direction Générale",
  OperationsSupplyChain: "Opérations & Supply Chain",
  QualityManager: "Responsable Qualité",
  SalesManagement: "Direction Commerciale",
  ITDepartment: "Service Informatique",
  FarmManager: "Responsable d'exploitation",
  WarehouseManager: "Responsable d'entrepôt",
  QualityAgent: "Agent Qualité",
  Worker: "Ouvrier",
};

export function getRoleLabel(role: string): string {
  return ROLE_LABELS[role] ?? role;
}