# ADR-0041: A decision is a payload — what needs you, with the paths open

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

### Amendment, 2026-09-14: the factory leads, one step at a time

Rod, the same day:

> the goal is to guide them through the process of building specs — not
> make them ask the right questions to the chat

> this will put all the specs into pending that you discover here, and then
> they will get approved in later process

So "Needs you" is not a list to work through but a walk the factory leads.
`df.intake.next` returns the **one** step due on an import, and the
conversation screen shows only that, with the button that takes it:

1. **Decide** — the next open question, as a decision card with its paths.
2. **Rebuild** — answers are in; the document's draft is re-extracted with
   them.
3. **Propose** — nothing blocks the document; its specs go into **pending**
   (a proposed amendment).
4. **Extract** — only when nothing above is due: read a document not yet
   read.

Documents go in corpus order, so the scope document is settled first and
every later answer is cheaper. The walk ends with every document pending.
**It never approves.** Approval is the later, separate process ADR-0035
describes, and nothing in the walk shortcuts it.

## Consequences
- The vocabulary has ten types. The count in ADR-0021 and the design
  system's docs is updated with it.
- Options are suggestions, not answers. A recommended option is the
  proposer's view and is labelled as such; choosing it is still a person's
  decision and is recorded as theirs.
- A decision on the wire has 2–4 options. Intake questions raised before
  paths existed have none until `df.intake.suggest_paths` runs; the guide
  shows such a question with "answer in your own words" and a "Suggest
  answers" button rather than inventing options on the client.
