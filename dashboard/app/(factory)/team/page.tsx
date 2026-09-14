"use client";

/**
 * Team.
 *
 * Every card states the model family it runs on (ADR-0028), because "which
 * model am I buying" is the question a price makes people ask, and a
 * persona that hides it is selling a name rather than a capability. The
 * assignment map underneath says *why* each member owns its stage —
 * verifiability decides, not seniority.
 */

import {
  Button,
  PersonaCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
  TeamMemberCard,
} from "@dark-factory/ui";

import { useDispatch, useQuery } from "@/lib/local/store";

export default function TeamPage() {
  const team = useQuery((db) => db.team);
  const project = useQuery((db) => db.project);
  const dispatch = useDispatch();

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 px-6 py-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold">Team</h1>
          <p className="mt-0.5 text-xs text-secondary">
            {project?.name} · team revision {team.revision} · {team.members.length} members · every
            run records the revision it ran with
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm">
            Revision history
          </Button>
          <Button variant="needs-you" size="sm">
            Hire
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        {team.members.map((member) => (
          <div key={member.id} className="flex flex-col gap-2">
            <TeamMemberCard member={member} />
            {overBudget(member.spend?.input_tokens, member.token_budget) ? (
              <Button
                size="sm"
                variant="needs-you"
                className="w-fit"
                onClick={() => dispatch("df.team.raise_budget", { id: member.id })}
              >
                Raise the cap
              </Button>
            ) : null}
          </div>
        ))}
      </div>

      <section className="flex flex-col gap-3">
        <div>
          <h2 className="text-sm font-semibold">Assignment map</h2>
          <p className="mt-0.5 text-xs text-secondary">
            Which member owns which stage and hook point. Verifiability decides, not seniority.
          </p>
        </div>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Stage or hook</TableHead>
              <TableHead>Member</TableHead>
              <TableHead>Deployment</TableHead>
              <TableHead>Why it sits here</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {team.assignments.map((assignment) => (
              <TableRow key={assignment.stage}>
                <TableCell className="font-mono text-2xs">{assignment.stage}</TableCell>
                <TableCell>
                  {assignment.member}
                  {assignment.human ? <span className="text-muted"> (human)</span> : null}
                </TableCell>
                <TableCell className="font-mono text-2xs text-secondary">
                  {assignment.deployment}
                </TableCell>
                <TableCell className="text-secondary">{assignment.why}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </section>

      <section className="flex flex-col gap-3">
        <div>
          <h2 className="text-sm font-semibold">Hire</h2>
          <p className="mt-0.5 text-xs text-secondary">
            Personas from the marketplace. Every card states the model family it runs on.
          </p>
        </div>
        <div className="grid grid-cols-3 gap-3">
          {team.hireable.map((listing) => (
            <PersonaCard
              key={listing.name}
              persona={{
                id: listing.name,
                name: listing.name,
                author: listing.author,
                description: listing.blurb,
                price_usd:
                  listing.price === "Free" ? null : Number(listing.price.replace(/\D/g, "")),
                installed: listing.state === "installed",
                avatar_url: "/design/portrait-sample.svg",
              }}
              modelFamily={listing.model_family}
              role={listing.role}
              speed={listing.speed as "quick" | "balanced" | "deliberate"}
              onTeam={listing.state === "on_team"}
              onInstall={() => dispatch("df.team.hire", { persona: listing.name })}
            />
          ))}
        </div>
      </section>
    </div>
  );
}

/** A member is over budget when its spend has passed an explicit cap. A
 *  null cap means the org default applies, which is not the same as none. */
function overBudget(used: number | undefined, budget: number | null | undefined): boolean {
  if (used === undefined || budget === null || budget === undefined) return false;
  return used > budget;
}
