import * as React from "react";
import { CheckIcon } from "lucide-react";

import { cn } from "../../lib/cn";
import { usd } from "../../lib/format";
import type { Persona, SpeedPreset } from "../../types/run";
import { Avatar, AvatarFallback, AvatarImage } from "../ui/avatar";
import { Badge } from "../ui/badge";
import { Button } from "../ui/button";
import { Card, CardContent, CardFooter } from "../ui/card";

export interface PersonaCardProps extends React.ComponentProps<"div"> {
  persona: Persona;
  /** Required on the card, per ADR-0028 — a price without a model is unreadable. */
  modelFamily: string;
  speed?: SpeedPreset;
  role?: string;
  onInstall?: (persona: Persona) => void;
}

/**
 * The marketplace face of a team member (ADR-0028, deferred).
 *
 * A persona is a role, a deployment, skills and a price under a name and a
 * portrait. The card states the underlying model family as a requirement of
 * the ADR rather than as a courtesy: two personas on the same model may
 * differ in price and depth only by their speed preset, and a buyer who
 * cannot see the model cannot tell whether they are paying for judgement or
 * for branding.
 *
 * Free and priced are the same layout with a different figure, deliberately.
 * Making the paid variant louder would turn a roster into a storefront, and
 * the community tier is a first-class plugin surface (ADR-0019), not a
 * lesser one.
 */
export function PersonaCard({
  persona,
  modelFamily,
  speed,
  role,
  onInstall,
  className,
  ...props
}: PersonaCardProps) {
  const free = persona.price_usd == null || persona.price_usd === 0;

  return (
    <Card
      data-slot="persona-card"
      data-installed={persona.installed || undefined}
      className={cn(persona.installed && "border-accent-border", className)}
      {...props}
    >
      <CardContent className="flex flex-col gap-3 pt-3.5">
        <div className="flex items-start gap-3">
          <Avatar className="size-12">
            {persona.avatar_url && <AvatarImage src={persona.avatar_url} alt="" />}
            <AvatarFallback>{persona.name.slice(0, 2).toUpperCase()}</AvatarFallback>
          </Avatar>

          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <span className="text-base leading-tight font-semibold text-primary">
              {persona.name}
            </span>
            <span className="text-2xs text-muted">by {persona.author}</span>
            <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
              {role && <Badge variant="outline">{role}</Badge>}
              {speed && <Badge>{speed}</Badge>}
            </div>
          </div>

          <div className="shrink-0 text-right">
            {free ? (
              <span className="text-sm font-medium text-secondary">Free</span>
            ) : (
              <>
                <div className="tnum text-base leading-none font-semibold text-primary">
                  {usd(persona.price_usd)}
                </div>
                <div className="text-2xs text-muted">per month</div>
              </>
            )}
          </div>
        </div>

        {persona.description && (
          <p className="text-sm text-secondary">{persona.description}</p>
        )}

        {/* ADR-0028 requires this. It is the one fact that makes a price
            comparable between two personas. */}
        <div className="flex items-baseline justify-between border-t border-border pt-2">
          <span className="text-2xs tracking-wide text-muted uppercase">Model</span>
          <span className="font-mono text-2xs text-secondary">{modelFamily}</span>
        </div>
      </CardContent>

      <CardFooter>
        {persona.installed ? (
          <Button variant="outline" size="sm" disabled>
            <CheckIcon /> Installed
          </Button>
        ) : (
          <Button
            variant={free ? "default" : "needs-you"}
            size="sm"
            onClick={() => onInstall?.(persona)}
          >
            {free ? "Add to team" : `Hire — ${usd(persona.price_usd)}/mo`}
          </Button>
        )}
      </CardFooter>
    </Card>
  );
}
