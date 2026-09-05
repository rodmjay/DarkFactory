"use client";

import * as React from "react";
import { InfoIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { at } from "../../lib/format";
import type { Provenance } from "../../types/run";
import { Popover, PopoverContent, PopoverTrigger } from "../ui/popover";
import { SpecId } from "./spec-id";

export interface ProvenancePopoverProps {
  provenance: Provenance;
  /** The thing being explained. Defaults to a small info button. */
  children?: React.ReactNode;
  align?: "start" | "center" | "end";
  /** Rendered open, for the showcase and for screenshot tests. */
  defaultOpen?: boolean;
}

/**
 * Why a thing exists.
 *
 * Every amendment carries provenance (ADR-0016) — conversation, turn,
 * proposer, approver — and a run's team snapshot records the skill
 * revisions it actually ran with (ADR-0028). All of it is already stored;
 * this is the one place it becomes answerable without a query.
 *
 * The same component attaches to a spec node and to a cost figure, because
 * "why does this rule exist" and "why did this cost that" have the same
 * answer shape: a conversation, an agent, a model, and a set of pinned
 * instructions. Splitting them into two components would be two half-answers
 * of the same question.
 *
 * Skill revisions are shown pinned rather than by name alone. A run that
 * behaved oddly is explained by the instructions it had, not the ones the
 * library holds today, and a bare name silently resolves to the latter.
 */
export function ProvenancePopover({
  provenance,
  children,
  align = "start",
  defaultOpen,
}: ProvenancePopoverProps) {
  return (
    <Popover defaultOpen={defaultOpen}>
      <PopoverTrigger asChild>
        {children ?? (
          <button
            type="button"
            aria-label="Why this exists"
            className={cn(
              "inline-flex size-4 items-center justify-center rounded-pill text-muted",
              "motion-fast transition-colors hover:text-primary",
              "outline-none focus-visible:outline-2 focus-visible:outline-offset-2",
              "focus-visible:outline-accent-ring",
            )}
          >
            <InfoIcon className="size-3.5" />
          </button>
        )}
      </PopoverTrigger>

      <PopoverContent align={align} className="w-80">
        <div className="flex flex-col gap-2.5">
          <span className="text-2xs font-medium tracking-wide text-muted uppercase">
            Why this exists
          </span>

          <dl className="grid grid-cols-[5.5rem_1fr] gap-x-3 gap-y-1.5 text-xs">
            {provenance.conversation_title && (
              <>
                <dt className="text-muted">Conversation</dt>
                <dd className="min-w-0 text-primary">
                  {provenance.conversation_title}
                  {provenance.turn_id && (
                    <span className="text-muted"> · turn {provenance.turn_id.slice(-4)}</span>
                  )}
                </dd>
              </>
            )}

            <dt className="text-muted">Proposed by</dt>
            <dd className="text-primary">
              {provenance.proposer.name}
              <span className="text-muted"> ({provenance.proposer.type})</span>
            </dd>

            {provenance.approver && (
              <>
                <dt className="text-muted">Approved by</dt>
                <dd className="text-primary">
                  {provenance.approver.name}
                  {provenance.approver.at && (
                    <span className="text-muted"> · {at(provenance.approver.at)}</span>
                  )}
                </dd>
              </>
            )}

            {(provenance.model_family || provenance.deployment) && (
              <>
                <dt className="text-muted">Model</dt>
                <dd className="font-mono text-2xs text-secondary">
                  {provenance.deployment}
                  {provenance.model_family && (
                    <span className="text-muted"> → {provenance.model_family}</span>
                  )}
                </dd>
              </>
            )}

            {provenance.at && (
              <>
                <dt className="text-muted">At</dt>
                <dd className="tnum text-secondary">{at(provenance.at)}</dd>
              </>
            )}
          </dl>

          {provenance.skill_revisions && provenance.skill_revisions.length > 0 && (
            <div className="flex flex-col gap-1 border-t border-border pt-2">
              <span className="text-2xs text-muted">
                Skill revisions in use at the time
              </span>
              <ul className="flex flex-wrap gap-1">
                {provenance.skill_revisions.map((skill) => (
                  <li
                    key={`${skill.name}@${skill.revision}`}
                    className="rounded-pill border border-border bg-sunken px-1.5 py-0.5 font-mono text-2xs text-secondary"
                  >
                    {skill.name}
                    <span className="text-muted">@{skill.revision.slice(0, 7)}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {provenance.conversation_id && (
            <div className="flex items-center gap-1.5 border-t border-border pt-2 text-2xs text-muted">
              conversation <SpecId id={provenance.conversation_id} />
            </div>
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}
