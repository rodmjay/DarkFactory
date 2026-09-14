# ADR-0039: A decision is a payload — what needs you, with the paths open

## Status
Accepted. Adds a tenth component type to
[ADR-0021](0021-rendering-vocabulary.md)'s vocabulary, through the
deliberate route that ADR requires: a schema, a design-system component,
a showcase entry.

## Context
Rod, 2026-09-14, after the architect answered "what's missing?" with nine
hundred words, eleven bullet points and three numbered questions:

> I would like to return structured json data using json schema and return
> UI that has actionable items that need to be done by me with potential
> paths, we must simplify this a lot

The factory had two sources of "this needs you" and rendered both as prose:

- **Intake questions** — 28 open on the first five extracted documents,
  each a sentence with no suggested answer, answerable only by typing.
- **The architect's replies** — correct, careful, and a wall of text whose
  actual content was three decisions buried at the end.

The vocabulary's `form` payload was the nearest fit and not a good one: it
has fields and a submit button, but no notion of options that carry
consequences, a recommendation, or leaving something open deliberately.

## Decision

**A decision is one shape everywhere** —
[contracts/schemas/decision.schema.json](../../contracts/schemas/decision.schema.json):
a title phrased as a question, why it needs deciding, 2–4 options each with
its consequence and at most one recommended, and whether answering in one's
own words or deferring is allowed.

**It is an ADR-0021 payload, `decision`,** rendered by a `DecisionCard` in
`@dark-factory/ui`: pick an option, write your own, or defer. The card does
not know what answering means; the screen that shows it does — answering an
intake question is `df.intake.answer`, answering the architect is the next
turn.

**Everything that needs a person becomes one.** Intake questions carry
options (suggested by a model where extraction did not supply them); the
architect's replies carry decisions and keep their prose to a few sentences.
One list — "Needs you" — shows them all.

## Consequences
- The vocabulary has ten types. The count in ADR-0021 and the design
  system's docs is updated with it.
- Options are suggestions, not answers. A recommended option is the
  proposer's view and is labelled as such; choosing it is still a person's
  decision and is recorded as theirs.
- A decision without options is not valid. A question the proposer cannot
  offer paths for is sent with "answer in your own words" as the only way,
  which the schema expresses as `allow_other` on a decision whose options
  are the honest minimum.
