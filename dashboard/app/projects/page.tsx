import Link from "next/link";
import { listActiveRuns, listProjects } from "@/lib/factory-api";

export default async function ProjectsPage() {
  const [projects, activeRuns] = await Promise.all([listProjects(), listActiveRuns()]);

  return (
    <div className="mx-auto max-w-4xl px-6 py-8">
      <section className="mb-8">
        <h2 className="mb-2 text-sm font-medium text-neutral-400">
          What&apos;s happening right now
        </h2>
        {activeRuns === null ? (
          <ApiNotWiredNotice endpoint="GET /api/runs?status=active" />
        ) : activeRuns.length === 0 ? (
          <p className="text-sm text-neutral-500">No active runs.</p>
        ) : (
          <ul className="space-y-2">
            {activeRuns.map((run) => (
              <li key={run.id} className="rounded border border-neutral-800 px-3 py-2 text-sm">
                <Link href={`/runs/${run.id}`} className="hover:underline">
                  {run.id}
                </Link>{" "}
                — {run.currentStage} ({run.status})
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h1 className="mb-4 text-lg font-semibold">Projects</h1>
        {projects === null ? (
          <ApiNotWiredNotice endpoint="GET /api/projects" />
        ) : projects.length === 0 ? (
          <p className="text-sm text-neutral-500">
            No projects registered yet. Run <code>projects.register(...)</code> from a connected
            host.
          </p>
        ) : (
          <ul className="space-y-2">
            {projects.map((project) => (
              <li key={project.id} className="rounded border border-neutral-800 px-3 py-2">
                <Link href={`/projects/${project.id}`} className="font-medium hover:underline">
                  {project.name}
                </Link>
                <span className="ml-2 text-sm text-neutral-500">
                  {project.activeRunCount} active run{project.activeRunCount === 1 ? "" : "s"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function ApiNotWiredNotice({ endpoint }: { endpoint: string }) {
  return (
    <p className="rounded border border-dashed border-neutral-800 px-3 py-2 text-sm text-neutral-500">
      {endpoint} isn&apos;t implemented yet — the front MCP surface and REST facade land in a
      later step. This page will populate once the factory API is reachable.
    </p>
  );
}
