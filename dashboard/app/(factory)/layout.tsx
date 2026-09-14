import { Suspense } from "react";
import type * as React from "react";

import { FactoryShell } from "@/components/shell";

/**
 * The eight product screens share one shell. `/design` deliberately sits
 * outside this group: the showcase is the design system's own page and
 * should not be framed by the product's navigation.
 */
export default function FactoryLayout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense>
      <FactoryShell>{children}</FactoryShell>
    </Suspense>
  );
}
