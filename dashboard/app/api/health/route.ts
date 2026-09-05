import { NextResponse } from "next/server";

// Liveness for the `dashboard` Compose service's health check. This is
// intentionally dependency-free (no call to the factory API) so the
// dashboard container can report healthy on its own; the pages themselves
// surface whether the factory API is reachable.
export function GET() {
  return NextResponse.json({ status: "ok" });
}
