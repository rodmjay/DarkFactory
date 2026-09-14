"use client";

/**
 * Building the specs, one decision at a time (ADR-0041).
 *
 * A person importing a body of specifications should not have to know what
 * to ask the architect. The factory knows what is next — `df.intake.next` —
 * so this panel shows exactly that one thing, with the paths open, and the
 * button that does it. Every step ends the same way: the document's specs
 * go into pending, where approval picks them up later. Nothing here
 * approves anything.
 */

import * as React from "react";
import { RefreshCw } from "lucide-react";

import { Badge, Button, Card, DecisionCard } from "@dark-factory/ui";
import type { Decision } from "@dark-factory/ui";

import type { IntakeNextStep } from "@/lib/factory/mcp";
import { useGuide, usePrototype } from "@/lib/local/store";

type Note = { tone: "ok" | "error"; text: string; at: Date };

export function GuidePanel() {
  const prototype = usePrototype();
  const guide = useGuide();
  const [running, setRunning] = React.useState<string | null>(null);
  const [note, setNote] = React.useState<Note | null>(null);

  if (prototype || !guide.hydrated || guide.step === null) return null;
  const step = guide.step;

  /** Runs one factory call, showing what is running and what came of it. */
  const run = async (label: string, done: string, call: () => Promise<{ ok: boolean; error?: string }>) => {
    setRunning(label);
    setNote(null);
    const result = await call();
    setRunning(null);
    setNote(result.ok ? { tone: "ok", text: done, at: new Date() } : { tone: "error", text: result.error ?? "It failed.", at: new Date() });
  };

  return (
    <div className="flex-none border-b border-border px-5 py-4">
      <div className="mx-auto flex max-w-3xl flex-col gap-3">
        <Progress step={step} />
        {running ? (
          <p className="flex items-center gap-2 text-xs text-secondary" role="status">
            <RefreshCw className="size-3.5 animate-spin" aria-hidden />
            {running}
          </p>
        ) : null}
        <Step step={step} busy={running !== null} run={run} />
        {note ? (
          <p className={note.tone === "ok" ? "text-xs text-secondary" : "text-xs text-danger"} role="status">
            {note.text} <span className="text-muted">· {note.at.toLocaleTimeString()}</span>
          </p>
        ) : null}
      </div>
    </div>
  );
}

function Progress({ step }: { step: IntakeNextStep }) {
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
      <span className="font-semibold">Building the specs</span>
      <span className="text-secondary">
        {step.settled} of {step.documents} documents done
      </span>
      {step.open_questions > 0 ? <span className="text-secondary">· {step.open_questions} decisions left</span> : null}
    </div>
  );
}

type Run = (label: string, done: string, call: () => Promise<{ ok: boolean; error?: string }>) => Promise<void>;

function Step({ step, busy, run }: { step: IntakeNextStep; busy: boolean; run: Run }) {
  const guide = useGuide();
  const title = step.source_title ?? step.source_ref ?? "This document";

  switch (step.step) {
    case "decide": {
      const question = step.question!;
      const options = question.options ?? [];
      const decision: Decision = {
        id: question.question_id,
        title: question.question,
        why: question.quote ? `About: “${question.quote}”` : undefined,
        source_ref: step.source_ref ?? undefined,
        kind: question.kind,
        options: options.map((o) => ({
          id: o.id,
          label: o.label,
          consequence: o.consequence ?? undefined,
          recommended: o.recommended || undefined,
        })),
        allow_other: true,
        allow_defer: true,
      };
      const answer = (text: string) =>
        run("Saving your answer…", "Answer saved.", () => guide.answer(question.question_id, text));

      return (
        <div className="flex flex-col gap-2">
          <StepLabel>
            {title} · decision 1 of {step.open_in_source}
          </StepLabel>
          <DecisionCard
            key={question.question_id}
            decision={decision}
            busy={busy}
            onChoose={(id) => {
              const chosen = options.find((o) => o.id === id);
              if (chosen) answer(chosen.consequence ? `${chosen.label}. ${chosen.consequence}` : chosen.label);
            }}
            onOther={answer}
            onDefer={(reason) =>
              run("Leaving it open…", "Left open. It won't block this document.", () =>
                guide.defer(question.question_id, reason),
              )
            }
          />
          {options.length === 0 && step.without_paths > 0 ? (
            <div className="flex items-center gap-3">
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                onClick={() =>
                  run(
                    `Suggesting answers for ${step.without_paths} decisions — a few minutes…`,
                    "Suggested answers are ready.",
                    () => guide.suggestPaths(step.intake_id),
                  )
                }
              >
                Suggest answers
              </Button>
              <span className="text-2xs text-muted">Or write your own above.</span>
            </div>
          ) : null}
        </div>
      );
    }

    case "rebuild":
      return (
        <Action
          label={title}
          text="Your answers are in. Rebuild the draft so they are part of it."
          button="Rebuild draft"
          busy={busy}
          onClick={() =>
            run("Rebuilding the draft — about two minutes…", "Draft rebuilt.", () => guide.rebuild(step.source_id!))
          }
        />
      );

    case "propose":
      return (
        <Action
          label={title}
          text={`Ready: ${step.nodes} specs, nothing left to decide. They go into pending; approving them comes later.`}
          button={`Put ${step.nodes} specs into pending`}
          busy={busy}
          onClick={() =>
            run("Putting them into pending…", `${step.nodes} specs are pending approval.`, () =>
              guide.propose(step.source_id!),
            )
          }
        />
      );

    case "extract":
      return (
        <Action
          label={title}
          text="Not read yet. Reading it drafts its specs and finds what still needs deciding."
          button="Read it now"
          busy={busy}
          onClick={() =>
            run("Reading the document — about two minutes…", "Read. Its decisions are next.", () =>
              guide.rebuild(step.source_id!),
            )
          }
        />
      );

    case "done":
      return (
        <Card className="flex items-center gap-3 px-4 py-3">
          <Badge variant="outline">done</Badge>
          <span className="text-sm">All {step.documents} documents are in pending, waiting for approval.</span>
        </Card>
      );
  }
}

function StepLabel({ children }: { children: React.ReactNode }) {
  return <p className="text-2xs font-medium uppercase tracking-wide text-muted">{children}</p>;
}

function Action({
  label,
  text,
  button,
  busy,
  onClick,
}: {
  label: string;
  text: string;
  button: string;
  busy: boolean;
  onClick: () => void;
}) {
  return (
    <Card className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <StepLabel>{label}</StepLabel>
        <p className="mt-1 text-sm">{text}</p>
      </div>
      <Button size="sm" disabled={busy} onClick={onClick}>
        {button}
      </Button>
    </Card>
  );
}
