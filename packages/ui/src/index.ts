/**
 * @dark-factory/ui — the Dark Factory design system (ADR-0033).
 *
 * The package holds visual code and nothing else: no data fetching, no
 * routing, no app state. Components take props; the app wires them.
 */

export { cn } from "./lib/cn";
export { FOCUS_RING, FOCUS_RING_INSET, DISABLED } from "./lib/styles";
export * from "./lib/vocabulary";

// ---- shadcn base set, restyled onto the tokens ----
export * from "./components/ui/avatar";
export * from "./components/ui/badge";
export * from "./components/ui/button";
export * from "./components/ui/card";
export * from "./components/ui/command";
export * from "./components/ui/dialog";
export * from "./components/ui/dropdown-menu";
export * from "./components/ui/input";
export * from "./components/ui/label";
export * from "./components/ui/popover";
export * from "./components/ui/scroll-area";
export * from "./components/ui/select";
export * from "./components/ui/separator";
export * from "./components/ui/sheet";
export * from "./components/ui/skeleton";
export * from "./components/ui/switch";
export * from "./components/ui/table";
export * from "./components/ui/tabs";
export * from "./components/ui/textarea";
export * from "./components/ui/toast";
export * from "./components/ui/tooltip";

// ---- domain ----
export * from "./components/domain/layer-badge";
export * from "./components/domain/spec-id";
export * from "./components/domain/status-chip";
