import * as React from "react";
import { CheckIcon, PauseIcon, PencilLineIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { FOCUS_RING } from "../../lib/styles";
import type { Decision } from "../../types/payload";
import { Badge } from "../ui/badge";
import { Button } from "../ui/button";
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from "../ui/card";
import { Textarea } from "../ui/textarea";

/** Where a decision stands. The card draws it; the screen owns it. */
export type DecisionState =
  | { status: "open" }
  /** `choice` is an option id, or the person's own words when it matches none. */
  | { status: "answered"; choice: string; by?: string }
  | { status: "deferred"; reason: string };

export interface DecisionCardProps extends Omit<React.ComponentProps<"div">, "children"> {
  /** The `decision` payload, with or without its `type`. */
  decision: Decision;
  state?: DecisionState;
  /** An answer is in flight: everything is disabled until the state comes back. */
  busy?: boolean;
  onChoose?: (optionId: string) => void;
  onOther?: (text: string) => void;
  onDefer?: (reason: string) => void;
  /**
   * Which way of answering is open on first render. For the showcase, which
   * cannot click, and for a screen restoring a half-written answer.
   */
  defaultMode?: "choose" | "other" | "defer";
}

type Mode = "choose" | "other" | "defer";

/**
 * Something a person has to decide, with the paths open to them (ADR-0041).
 *
 * The card does not know what answering means. Choosing an option, writing
 * one's own, or leaving it open call back to the screen that shows it —
 * answering an intake question is `df.intake.answer`, answering the architect
 * is the next turn — and the result comes back as `state`. With no callbacks
 * it is read-only, which is how `PayloadRenderer` draws it.
 *
 * A recommended option is marked and never preselected. It is the proposer's
 * view; choosing it is still the person's decision, and a card that arrived
 * with it already selected would make "approve the default" the path of
 * least resistance for every question in the list.
 *
 * Leaving it open asks for a reason, for the same reason a rejection does: a
 * decision deferred with nothing said is one somebody has to rediscover.
 */
export function DecisionCard({
  decision,
  state = { status: "open" },
  busy = false,
  onChoose,
  onOther,
  onDefer,
  defaultMode = "choose",
  className,
  ...props
}: DecisionCardProps) {
  const uid = React.useId();
  const titleId = `${uid}-title`;
  const whyId = `${uid}-why`;

  if (state.status !== "open") {
    return (
      <Card
        data-slot="decision-card"
        data-status={state.status}
        role="group"
        aria-labelledby={titleId}
        className={cn(
          state.status === "answered" && "border-status-passed-border",
          className,
        )}
        {...props}
      >
        <div className="flex flex-col gap-2.5 px-4 py-3">
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 flex-col gap-1">
              <Meta kind={decision.kind} sourceRef={decision.source_ref} />
              <span id={titleId} className="text-sm font-medium text-primary">
                {decision.title}
              </span>
            </div>
            <StatusPill status={state.status} />
          </div>
          {state.status === "answered" ? (
            <Answered decision={decision} choice={state.choice} by={state.by} />
          ) : (
            <div className="rounded-control bg-sunken px-2.5 py-2">
              <div className="text-2xs tracking-wide text-muted uppercase">Left open because</div>
              <p className="mt-0.5 text-sm whitespace-pre-wrap text-secondary">{state.reason}</p>
            </div>
          )}
        </div>
      </Card>
    );
  }

  return (
    <OpenDecision
      decision={decision}
      busy={busy}
      onChoose={onChoose}
      onOther={onOther}
      onDefer={onDefer}
      defaultMode={defaultMode}
      uid={uid}
      titleId={titleId}
      whyId={whyId}
      className={className}
      {...props}
    />
  );
}

function OpenDecision({
  decision,
  busy,
  onChoose,
  onOther,
  onDefer,
  defaultMode,
  uid,
  titleId,
  whyId,
  className,
  ...props
}: Omit<DecisionCardProps, "state"> & {
  busy: boolean;
  defaultMode: Mode;
  uid: string;
  titleId: string;
  whyId: string;
}) {
  const allowOther = decision.allow_other !== false;
  const allowDefer = decision.allow_defer !== false;

  const initial: Mode =
    (defaultMode === "other" && allowOther) || (defaultMode === "defer" && allowDefer)
      ? defaultMode
      : "choose";
  const [mode, setMode] = React.useState<Mode>(initial);
  // Focus the textarea when a person opens it, not when it arrives open —
  // a card that grabs focus on render steals it from whatever they were on.
  const [openedByUser, setOpenedByUser] = React.useState(false);

  const canChoose = Boolean(onChoose) && !busy;

  const openMode = (next: Mode) => {
    setOpenedByUser(true);
    setMode(next);
  };

  return (
    <Card
      data-slot="decision-card"
      data-status="open"
      role="group"
      aria-labelledby={titleId}
      aria-describedby={decision.why ? whyId : undefined}
      aria-busy={busy || undefined}
      className={cn("border-accent-border", className)}
      {...props}
    >
      <CardHeader className="gap-1.5">
        <Meta kind={decision.kind} sourceRef={decision.source_ref} />
        <div className="flex items-start justify-between gap-3">
          <CardTitle id={titleId}>{decision.title}</CardTitle>
          <StatusPill status="open" />
        </div>
        {decision.why && (
          <p id={whyId} className="text-sm text-secondary">
            {decision.why}
          </p>
        )}
      </CardHeader>

      <CardContent>
        <div role="group" aria-label="Options" className="flex flex-col gap-2">
          {decision.options.map((option, index) => {
            const labelId = `${uid}-option-${index}`;
            const noteId = `${uid}-option-${index}-note`;
            const recId = `${uid}-option-${index}-rec`;
            const describedBy = [option.recommended && recId, option.consequence && noteId]
              .filter(Boolean)
              .join(" ");

            return (
              <button
                key={option.id}
                type="button"
                data-recommended={option.recommended || undefined}
                disabled={!canChoose}
                aria-labelledby={labelId}
                aria-describedby={describedBy || undefined}
                onClick={() => onChoose?.(option.id)}
                className={cn(
                  "flex w-full flex-col items-start gap-1 rounded-control border px-3 py-2.5 text-left",
                  "border-border-strong bg-raised motion-fast transition-colors",
                  "enabled:hover:border-accent-border enabled:hover:bg-sunken",
                  // Read-only keeps full contrast: a consequence dimmed to
                  // half opacity is a consequence nobody reads. Only an
                  // answer in flight fades the options.
                  "disabled:cursor-default",
                  busy && "opacity-60",
                  FOCUS_RING,
                )}
              >
                <span className="flex w-full items-start justify-between gap-3">
                  <span id={labelId} className="text-sm font-medium text-primary">
                    {option.label}
                  </span>
                  {option.recommended && (
                    <Badge id={recId} variant="accent">
                      Recommended
                    </Badge>
                  )}
                </span>
                {option.consequence && (
                  <span id={noteId} className="text-xs text-secondary">
                    {option.consequence}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </CardContent>

      {(allowOther || allowDefer) && (
        <CardFooter className={cn(mode !== "choose" && "flex-col items-stretch")}>
          {mode === "choose" && (
            <>
              {allowOther && (
                <Button
                  variant="ghost"
                  size="sm"
                  aria-expanded={false}
                  disabled={!onOther || busy}
                  onClick={() => openMode("other")}
                >
                  <PencilLineIcon /> Answer in my own words
                </Button>
              )}
              {allowDefer && (
                <Button
                  variant="ghost"
                  size="sm"
                  aria-expanded={false}
                  disabled={!onDefer || busy}
                  onClick={() => openMode("defer")}
                >
                  <PauseIcon /> Leave open
                </Button>
              )}
            </>
          )}
          {mode === "other" && (
            <Compose
              label="Your answer"
              placeholder="Answer in your own words"
              submitLabel="Send"
              autoFocus={openedByUser}
              disabled={!onOther || busy}
              onSubmit={(text) => onOther?.(text)}
              onCancel={() => setMode("choose")}
            />
          )}
          {mode === "defer" && (
            <Compose
              label="Why leave it open?"
              placeholder="A short reason — what it is waiting on"
              submitLabel="Leave open"
              autoFocus={openedByUser}
              disabled={!onDefer || busy}
              onSubmit={(reason) => onDefer?.(reason)}
              onCancel={() => setMode("choose")}
            />
          )}
        </CardFooter>
      )}
    </Card>
  );
}

/**
 * A textarea with a send and a cancel. Enter with ⌘ or Ctrl sends; Escape
 * goes back to the options. Empty text cannot be sent — an empty answer and
 * an empty reason are both the thing this card exists to prevent.
 */
function Compose({
  label,
  placeholder,
  submitLabel,
  autoFocus,
  disabled,
  onSubmit,
  onCancel,
}: {
  label: string;
  placeholder: string;
  submitLabel: string;
  autoFocus: boolean;
  disabled: boolean;
  onSubmit: (text: string) => void;
  onCancel: () => void;
}) {
  const [text, setText] = React.useState("");
  const empty = text.trim().length === 0;

  const submit = () => {
    if (!empty && !disabled) onSubmit(text.trim());
  };

  return (
    <div className="flex flex-col gap-2">
      <Textarea
        aria-label={label}
        placeholder={placeholder}
        value={text}
        autoFocus={autoFocus}
        disabled={disabled}
        onChange={(event) => setText(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Escape") onCancel();
          if (event.key === "Enter" && (event.metaKey || event.ctrlKey)) {
            event.preventDefault();
            submit();
          }
        }}
      />
      <div className="flex items-center gap-2">
        <Button size="sm" disabled={empty || disabled} onClick={submit}>
          {submitLabel}
        </Button>
        <Button variant="ghost" size="sm" onClick={onCancel}>
          Back to the options
        </Button>
      </div>
    </div>
  );
}

function Answered({
  decision,
  choice,
  by,
}: {
  decision: Decision;
  choice: string;
  by?: string;
}) {
  const option = decision.options.find((o) => o.id === choice);

  return (
    <>
      <div className="rounded-control bg-sunken px-2.5 py-2">
        {option ? (
          <>
            <div className="flex items-center gap-1.5 text-sm font-medium text-primary">
              <CheckIcon className="size-3.5 text-status-passed-text" aria-hidden />
              {option.label}
            </div>
            {option.consequence && (
              <p className="mt-0.5 pl-5 text-xs text-secondary">{option.consequence}</p>
            )}
          </>
        ) : (
          <>
            <div className="text-2xs tracking-wide text-muted uppercase">In their own words</div>
            <p className="mt-0.5 text-sm whitespace-pre-wrap text-primary">{choice}</p>
          </>
        )}
      </div>
      {(by || option?.recommended) && (
        <p className="text-2xs text-muted">
          {by && <>Decided by {by}</>}
          {by && option?.recommended && " · "}
          {option?.recommended && "the proposer's recommendation"}
        </p>
      )}
    </>
  );
}

function Meta({ kind, sourceRef }: { kind?: string; sourceRef?: string }) {
  if (!kind && !sourceRef) return null;
  return (
    <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
      {kind && <Badge>{kind}</Badge>}
      {sourceRef && (
        <span className="min-w-0 truncate font-mono text-2xs text-muted">{sourceRef}</span>
      )}
    </div>
  );
}

function StatusPill({ status }: { status: DecisionState["status"] }) {
  const map = {
    open: "border-accent-border bg-accent-fill text-accent-text",
    answered: "border-status-passed-border bg-status-passed-fill text-status-passed-text",
    deferred: "border-status-pending-border bg-status-pending-fill text-status-pending-text",
  } as const;
  const label = { open: "needs you", answered: "decided", deferred: "left open" } as const;

  return (
    <span
      className={cn(
        "shrink-0 rounded-pill border px-2 py-0.5 text-2xs font-medium whitespace-nowrap",
        map[status],
      )}
    >
      {label[status]}
    </span>
  );
}
