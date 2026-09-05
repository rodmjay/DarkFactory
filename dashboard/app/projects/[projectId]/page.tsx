import Link from "next/link";
import { getProjectRuns } from "@/lib/factory-api";

export default async function ProjectPage({
  params,
}: {
  params: Promise<{ projectId: string }>;
}) {
  const { projectId } = await params;
  const runs = await getProjectRuns(projectId);

  return (
    <div className="mx-auto max-w-4xl px-6 py-8">
      <h1 className="mb-1 text-lg font-semibold">Project {projectId}</h1>
      <p className="mb-6 text-sm text-muted">Runs, newest first.</p>

      {runs === null ? (
        <p className="rounded border border-dashed border-border px-3 py-2 text-sm text-muted">
          GET /api/projects/{projectId}/runs isn&apos;t implemented yet — the front MCP surface
          and REST facade land in a later step.
        </p>
      ) : runs.length === 0 ? (
        <p className="text-sm text-muted">
          No runs yet. Submit one with <code>work.submit(project_id, input)</code>.
        </p>
      ) : (
        <ul className="space-y-2">
          {runs.map((run) => (
            <li key={run.id} className="rounded border border-border px-3 py-2 text-sm">
              <Link href={`/runs/${run.id}`} className="hover:underline">
                {run.id}
              </Link>{" "}
              — {run.currentStage} ({run.status})
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
