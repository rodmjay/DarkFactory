import { getRun } from "@/lib/factory-api";

const STAGES = ["intake", "spec", "plan", "implement", "verify", "ship"] as const;

export default async function RunPage({ params }: { params: Promise<{ runId: string }> }) {
  const { runId } = await params;
  const run = await getRun(runId);

  return (
    <div className="mx-auto max-w-4xl px-6 py-8">
      <h1 className="mb-1 text-lg font-semibold">Run {runId}</h1>
      <p className="mb-6 text-sm text-neutral-500">
        Stage timeline, artifacts, gates, and the live event stream.
      </p>

      <section className="mb-8">
        <h2 className="mb-2 text-sm font-medium text-neutral-400">Stage timeline</h2>
        <ol className="flex flex-wrap gap-2">
          {STAGES.map((stage) => (
            <li
              key={stage}
              className="rounded border border-neutral-800 px-3 py-1 text-sm text-neutral-400"
            >
              {stage}
              {run?.currentStage === stage ? " (current)" : ""}
            </li>
          ))}
        </ol>
        {run === null && (
          <p className="mt-2 rounded border border-dashed border-neutral-800 px-3 py-2 text-sm text-neutral-500">
            GET /api/runs/{runId} isn&apos;t implemented yet, so live stage/status/gate state
            isn&apos;t shown. Real-time updates arrive over the SignalR hub once the engine and
            dashboard wiring (steps 2 and 4) land.
          </p>
        )}
      </section>

      <section className="mb-8">
        <h2 className="mb-2 text-sm font-medium text-neutral-400">Artifacts</h2>
        <p className="text-sm text-neutral-500">
          Spec / Plan / ChangeSet / TestReport artifacts will appear here with a JSON viewer,
          resolved by reference from the factory&apos;s artifact store.
        </p>
      </section>

      <section className="mb-8">
        <h2 className="mb-2 text-sm font-medium text-neutral-400">Gate</h2>
        <div className="flex gap-2">
          <button
            disabled
            className="cursor-not-allowed rounded border border-neutral-800 px-3 py-1 text-sm text-neutral-600"
          >
            Approve
          </button>
          <button
            disabled
            className="cursor-not-allowed rounded border border-neutral-800 px-3 py-1 text-sm text-neutral-600"
          >
            Reject
          </button>
        </div>
        <p className="mt-2 text-sm text-neutral-500">
          Wired to work.approve / work.reject via the factory&apos;s REST facade once it exists.
        </p>
      </section>

      <section>
        <h2 className="mb-2 text-sm font-medium text-neutral-400">Event stream</h2>
        <p className="text-sm text-neutral-500">
          Live via the SignalR hub at <code>/hubs/runs</code> once the dashboard subscribes to it.
        </p>
      </section>
    </div>
  );
}
