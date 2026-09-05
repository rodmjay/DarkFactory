import * as React from "react";
import { ServerIcon, SparklesIcon, UserIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { tokens, usd } from "../../lib/format";
import type { TeamMember } from "../../types/run";
import { Avatar, AvatarFallback, AvatarImage } from "../ui/avatar";
import { Badge } from "../ui/badge";
import { Card, CardContent } from "../ui/card";
import { CostBar } from "./cost-bar";

export interface TeamMemberCardProps extends React.ComponentProps<"div"> {
  member: TeamMember;
}

const KIND_LABEL = {
  native: "native agent",
  agent_server: "agent server",
  persona: "persona",
} as const;

/**
 * One seat on the project's standing team (ADR-0028).
 *
 * Native agents and agent servers share a seat in the assignment map, so
 * they share a card — the kind is a badge rather than a different layout,
 * because from the team's point of view they are the same thing: something
 * that does a stage, on a budget, with skills.
 *
 * The model family is always shown, including for a persona. ADR-0028 makes
 * that a requirement rather than a nicety: a persona is a name and a price
 * over a model somebody else chose, and hiding which model would make the
 * price unreadable.
 *
 * Over-budget is a card-level state, not a number in a corner. It is one of
 * the two things in the whole system that mean a person must act, so it
 * takes the accent's family and says how far over.
 */
export function TeamMemberCard({ member, className, ...props }: TeamMemberCardProps) {
  const spent =
    member.spend ? member.spend.input_tokens + member.spend.output_tokens : 0;
  const overBudget = member.token_budget != null && spent > member.token_budget;

  const KindIcon =
    member.kind === "agent_server" ? ServerIcon : member.kind === "persona" ? SparklesIcon : UserIcon;

  return (
    <Card
      data-slot="team-member-card"
      data-kind={member.kind}
      data-over-budget={overBudget || undefined}
      className={cn(
        overBudget && "border-status-over-budget-border bg-status-over-budget-fill",
        className,
      )}
      {...props}
    >
      <CardContent className="flex flex-col gap-3 pt-3.5">
        <div className="flex items-start gap-3">
          <Avatar className="size-10">
            {(member.avatar_url ?? member.persona?.avatar_url) && (
              <AvatarImage src={member.avatar_url ?? member.persona?.avatar_url} alt="" />
            )}
            <AvatarFallback>
              {(member.persona?.name ?? member.role).slice(0, 2).toUpperCase()}
            </AvatarFallback>
          </Avatar>

          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-sm font-semibold text-primary">
                {member.persona?.name ?? member.role}
              </span>
              {member.persona && (
                <span className="text-2xs text-muted">as {member.role}</span>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-1.5">
              <Badge variant="outline" className="gap-1">
                <KindIcon aria-hidden />
                {KIND_LABEL[member.kind]}
              </Badge>
              {member.model_family && (
                <span className="font-mono text-2xs text-secondary">{member.model_family}</span>
              )}
              {member.speed && (
                <Badge variant="default" title="Maps to the gateway's thinking budget (ADR-0028).">
                  {member.speed}
                </Badge>
              )}
            </div>

            <span className="font-mono text-2xs text-muted">
              deployment: {member.deployment}
            </span>
          </div>
        </div>

        {member.skills && member.skills.length > 0 && (
          <div className="flex flex-col gap-1">
            <span className="text-2xs tracking-wide text-muted uppercase">Skills</span>
            <ul className="flex flex-wrap gap-1">
              {member.skills.map((skill) => (
                <li key={`${skill.name}@${skill.revision}`}>
                  {/* Pinned by revision, so a run's behaviour is traceable to
                      the instructions it actually had (ADR-0028). */}
                  <span className="inline-flex items-center gap-1 rounded-pill border border-border bg-sunken px-1.5 py-0.5 font-mono text-2xs text-secondary">
                    {skill.name}
                    <span className="text-muted">@{skill.revision.slice(0, 7)}</span>
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {member.spend && (
          <CostBar
            compact
            budget={member.token_budget ?? undefined}
            segments={[
              {
                label: "input",
                tokens: member.spend.input_tokens,
                usd: null,
                over_budget: overBudget,
              },
              {
                label: "output",
                tokens: member.spend.output_tokens,
                usd: member.spend.usd ?? null,
                over_budget: overBudget,
              },
            ]}
          />
        )}

        {overBudget && (
          <p className="text-2xs text-status-over-budget-text">
            Over budget by {tokens(spent - (member.token_budget ?? 0))} tokens. Raise the cap or
            split the work — retrying will not help.
          </p>
        )}

        {member.persona?.price_usd != null && (
          <div className="flex items-baseline justify-between border-t border-border pt-2">
            <span className="text-2xs text-muted">by {member.persona.author}</span>
            <span className="tnum text-sm font-medium text-primary">
              {usd(member.persona.price_usd)}
              <span className="text-2xs font-normal text-muted">/mo</span>
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
