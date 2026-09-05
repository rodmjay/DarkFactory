import * as React from "react";
import { CheckIcon, ClockIcon, XIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { at } from "../../lib/format";
import type { Approval } from "../../types/run";
import { Avatar, AvatarFallback, AvatarImage } from "../ui/avatar";
import { Button } from "../ui/button";
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from "../ui/card";
import { Textarea } from "../ui/textarea";

export interface ApprovalCardProps extends Omit<React.ComponentProps<"div">, "onSubmit"> {
  approval: Approval;
  /** Whether the current viewer is one of the required approvers. */
  canDecide?: boolean;
  onApprove?: (reason: string) => void;
  onReject?: (reason: string) => void;
}

/**
 * A gate, with who has to clear it.
 *
 * Approvers are configurable per project and may be several (ADR-0017), so
 * the roster is part of the card rather than a detail behind it: the useful
 * question when a batch is stuck is usually "who else" and not "what".
 *
 * Rejection requires a reason and approval does not. That asymmetry is
 * deliberate — a rejection that says only "no" sends the proposer back to
 * guess, and the reason is the whole content of the decision. Approval's
 * reason is optional because the diff already says what was agreed to.
 */
export function ApprovalCard({
  approval,
  canDecide = true,
  onApprove,
  onReject,
  className,
  ...props
}: ApprovalCardProps) {
  const [reason, setReason] = React.useState("");

  const decided = approval.status !== "awaiting";
  const others = approval.required_approvers.filter((a) => !a.decision);
  const waitingOnOthers = !decided && !canDecide;

  return (
    <Card
      data-slot="approval-card"
      data-status={approval.status}
      className={cn(
        approval.status === "approved" && "border-status-passed-border",
        approval.status === "rejected" && "border-status-failed-border",
        !decided && "border-accent-border",
        className,
      )}
      {...props}
    >
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <CardTitle>{approval.summary}</CardTitle>
          <StatusPill status={approval.status} />
        </div>
      </CardHeader>

      <CardContent className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <span className="text-2xs tracking-wide text-muted uppercase">
            Required approvers
          </span>
          <ul className="flex flex-col gap-1.5">
            {approval.required_approvers.map((approver) => (
              <li key={approver.id} className="flex items-center gap-2 text-sm">
                <Avatar className="size-5 rounded-pill">
                  {approver.avatar_url && <AvatarImage src={approver.avatar_url} alt="" />}
                  <AvatarFallback className="text-[9px]">
                    {approver.name.slice(0, 2).toUpperCase()}
                  </AvatarFallback>
                </Avatar>
                <span className="text-primary">{approver.name}</span>
                {approver.decision ? (
                  <span
                    className={cn(
                      "inline-flex items-center gap-1 text-2xs",
                      approver.decision === "approved"
                        ? "text-status-passed-text"
                        : "text-status-failed-text",
                    )}
                  >
                    {approver.decision === "approved" ? (
                      <CheckIcon className="size-3" aria-hidden />
                    ) : (
                      <XIcon className="size-3" aria-hidden />
                    )}
                    {approver.decision}
                    {approver.decided_at && (
                      <span className="text-muted">· {at(approver.decided_at)}</span>
                    )}
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 text-2xs text-muted">
                    <ClockIcon className="size-3" aria-hidden />
                    waiting
                  </span>
                )}
              </li>
            ))}
          </ul>
        </div>

        {approval.reason && (
          <p className="rounded-control bg-sunken px-2.5 py-2 text-sm text-secondary">
            {approval.reason}
          </p>
        )}

        {!decided && canDecide && (
          <Textarea
            aria-label="Reason"
            placeholder="Reason (required to reject)"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        )}

        {waitingOnOthers && (
          <p className="text-sm text-secondary">
            Waiting on {others.map((a) => a.name).join(", ") || "another approver"}. You are not
            a required approver for this gate.
          </p>
        )}
      </CardContent>

      {!decided && canDecide && (
        <CardFooter>
          <Button variant="needs-you" size="sm" onClick={() => onApprove?.(reason)}>
            <CheckIcon /> Approve
          </Button>
          <Button
            variant="outline"
            size="sm"
            // A rejection with no reason is a dead end for the proposer.
            disabled={reason.trim().length === 0}
            title={reason.trim().length === 0 ? "A rejection needs a reason." : undefined}
            onClick={() => onReject?.(reason)}
          >
            Reject
          </Button>
        </CardFooter>
      )}
    </Card>
  );
}

function StatusPill({ status }: { status: Approval["status"] }) {
  const map = {
    awaiting: "border-accent-border bg-accent-fill text-accent-text",
    approved: "border-status-passed-border bg-status-passed-fill text-status-passed-text",
    rejected: "border-status-failed-border bg-status-failed-fill text-status-failed-text",
  } as const;

  return (
    <span
      className={cn(
        "shrink-0 rounded-pill border px-2 py-0.5 text-2xs font-medium whitespace-nowrap",
        map[status],
      )}
    >
      {status}
    </span>
  );
}
