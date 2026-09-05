import { z } from "zod";

// Mirrors contracts/schemas/envelope.schema.json (docs/conventions/envelope.md).
// Optional here so the server is also usable for manual testing directly
// from a host (e.g. Claude Code) that isn't the factory and doesn't supply
// one; a real Dark Factory-mediated call always includes it, and it is
// echoed back unchanged when present.
export const envelopeSchema = z
  .object({
    run_id: z.string(),
    stage_id: z.string(),
    project_id: z.string(),
    idempotency_key: z.string(),
    trace_id: z.string(),
    deadline: z.string(),
    callback_token: z.string().optional(),
  })
  .optional();

export type Envelope = z.infer<typeof envelopeSchema>;

export function echoEnvelope(envelope: Envelope) {
  return envelope ? { envelope } : {};
}
