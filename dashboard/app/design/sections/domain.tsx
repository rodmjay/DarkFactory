"use client";

import * as React from "react";
import {
  AmendmentRow,
  ApprovalCard,
  BatchCard,
  CodeDiff,
  CostBar,
  DependencyGraph,
  MetricTile,
  PayloadRenderer,
  PersonaCard,
  ProvenancePopover,
  SYNC_STATES,
  ServerCard,
  SpecDiff,
  SpecNodeCard,
  StageTimeline,
  SyncStatus,
  TeamMemberCard,
  type Payload,
} from "@dark-factory/ui";

import { Block, Frame, Row, Section } from "../parts";
import * as fixture from "../fixtures";

/* Every state in the step 2 brief has a frame here, labelled with the state
 * name. The labels are not decoration: this page is the living specification,
 * so a state that is not on it is not in the system, and an unlabelled
 * specimen cannot be checked against the list. */

export function DomainComponents() {
  return (
    <Section
      id="domain"
      title="Domain components"
      note="What the product is actually made of. Typed against contracts/schemas/ where a schema exists — today that is spec_diff and describe — and against types proposed in packages/ui/src/types where one does not. Fixtures are the real 3d acceptance run's ids and token counts, not invented ones."
    >
      <Block
        title="StageTimeline"
        note="plan → implement → verify → ship for one run. Ship is shown but is a batch action (ADR-0029) — a run that verifies clean is deployable, not deployed. A parked stage opens with the agent's question inline, because a run parked overnight because nobody expanded the row is the failure this exists to prevent."
      >
        <div className="grid gap-3 lg:grid-cols-2">
          <Frame label="running — plan passed, implement in flight">
            <StageTimeline run_id={fixture.RUN_ID} snapshot_id={fixture.SNAPSHOT_ID} stages={fixture.timelineStages.running} />
          </Frame>
          <Frame label="parked — the agent is waiting on a person">
            <StageTimeline run_id={fixture.RUN_ID} stages={fixture.timelineStages.parked} onAnswer={() => {}} />
          </Frame>
          <Frame label="failed — verify went red">
            <StageTimeline run_id={fixture.RUN_ID} stages={fixture.timelineStages.failed} />
          </Frame>
          <Frame label="over-budget — needs a person, and is not the same as failed">
            <StageTimeline run_id={fixture.RUN_ID} stages={fixture.timelineStages.overBudget} onAnswer={() => {}} />
          </Frame>
          <Frame label="completed — every stage passed" className="lg:col-span-2">
            <StageTimeline run_id={fixture.RUN_ID} snapshot_id={fixture.SNAPSHOT_ID} stages={fixture.timelineStages.shipped} />
          </Frame>
        </div>
      </Block>

      <Block
        title="SpecNodeCard"
        note="One small-grained node (ADR-0016). The text is the subject and gets the size; id, layer and edge counts are apparatus. `retired` stays legible because append-only means it is still there; `drifted` says the code no longer matches (ADR-0024) — computed, not prevented."
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <Frame label="default">
            <SpecNodeCard node={fixture.specNodes.default} />
          </Frame>
          <Frame label="selected">
            <SpecNodeCard node={fixture.specNodes.selected} selected />
          </Frame>
          <Frame label="retired">
            <SpecNodeCard node={fixture.specNodes.retired} />
          </Frame>
          <Frame label="drifted">
            <SpecNodeCard node={fixture.specNodes.drifted} />
          </Frame>
        </div>
        <Frame label="with a ProvenancePopover attached">
          <div className="flex items-start gap-2">
            <SpecNodeCard node={fixture.specNodes.default} className="flex-1" />
            <ProvenancePopover provenance={fixture.provenance} />
          </div>
        </Frame>
        <Frame label="revision history — oldest first, each with its own reason">
          <SpecNodeCard node={fixture.specNodes.withHistory} />
        </Frame>
        <p className="text-2xs text-muted">
          Three revisions, three rationale states: a reason, none at all, and the current one.
          `actor_id` and `approved_by` stay separate even where they match — approval is
          single-actor today and ADR-0017 makes approvers configurable per project. A revision
          whose reason changed but whose text did not will never appear, because rationale is
          not part of what a revision is hashed from.
        </p>
      </Block>

      <Block
        title="SpecDiff"
        note="Conflicts come first and are the loudest thing the system draws. A reader scanning for what is new will scroll past a conflict buried under the additions, and 'this contradicts the rule you set in March' is the single most valuable sentence the product says."
      >
        <div className="grid gap-3 lg:grid-cols-2">
          <Frame label="with conflicts">
            <SpecDiff
              diff={fixture.specDiff}
              conflicts={fixture.conflicts}
              summary="Creates 1, revises 1, retires 1, and conflicts with a decision made on 14 March."
            />
          </Frame>
          <Frame label="without conflicts">
            <SpecDiff diff={fixture.specDiff} summary="Creates 1, revises 1, retires 1." />
          </Frame>
          <Frame label="empty — nothing to approve" className="lg:col-span-2">
            <SpecDiff diff={fixture.emptyDiff} />
          </Frame>
        </div>
        <p className="text-2xs text-muted">
          Every element and every edge change carries its reason. The three states are
          distinguishable and deliberately do not collapse: a reason, “No reason given” where
          the key is absent, and “Reason left blank” where someone was asked and left it empty —
          the second means a prompt that never asked, the third means a person to go and ask.
          Reasons are never truncated; the ones the architect writes run 150–250 characters and
          the second sentence is usually the half worth reading. The edge line resolves{" "}
          <code className="font-mono">new:0</code> to the node this amendment creates, because
          “new:0 depends_on new:1” is not a sentence anyone can read.
        </p>
      </Block>

      <Block
        title="ApprovalCard"
        note="Approvers are configurable per project and may be several (ADR-0017), so the roster is on the card rather than behind it. Rejection requires a reason and approval does not — a rejection that says only 'no' sends the proposer back to guess."
      >
        <div className="grid gap-3 lg:grid-cols-2">
          <Frame label="awaiting">
            <ApprovalCard approval={fixture.approvals.awaiting} />
          </Frame>
          <Frame label="waiting on others — viewer is not a required approver">
            <ApprovalCard approval={fixture.approvals.waitingOnOthers} canDecide={false} />
          </Frame>
          <Frame label="approved">
            <ApprovalCard approval={fixture.approvals.approved} />
          </Frame>
          <Frame label="rejected">
            <ApprovalCard approval={fixture.approvals.rejected} />
          </Frame>
        </div>
      </Block>

      <Block
        title="AmendmentRow"
        note="A backlog item, draggable into a batch (ADR-0029). The handle is always visible: ordering is the interaction on this screen, and a control you have to hover to find reads as absent on a list of forty."
      >
        <Frame>
          <div className="flex flex-col gap-2">
            <AmendmentRow amendment={fixture.amendments.default} />
            <AmendmentRow amendment={fixture.amendments.inBatch} inBatch seq={1} />
            <AmendmentRow amendment={fixture.amendments.blocked} />
          </div>
        </Frame>
      </Block>

      <Block
        title="BatchCard"
        note="Deploy lives here and nowhere on a run, because deploy is a batch action (ADR-0029). Its placement is the ADR made visible — a deploy button on a run would mean the schema was lying about what a release is."
      >
        <div className="grid gap-3 lg:grid-cols-2">
          <Frame label="composing">
            <BatchCard batch={fixture.batches.composing} />
          </Frame>
          <Frame label="running">
            <BatchCard batch={fixture.batches.running} />
          </Frame>
          <Frame label="blocked by verify">
            <BatchCard batch={fixture.batches.blocked} />
          </Frame>
          <Frame label="deployable">
            <BatchCard batch={fixture.batches.deployable} />
          </Frame>
          <Frame label="deployed" className="lg:col-span-2">
            <BatchCard batch={fixture.batches.deployed} />
          </Frame>
        </div>
      </Block>

      <Block
        title="TeamMemberCard"
        note="Native agents and agent servers share a seat in the assignment map (ADR-0028), so they share a card — the kind is a badge, not a different layout. The model family is always shown, including under a persona, because a price without a model is unreadable."
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <Frame label="native agent">
            <TeamMemberCard member={fixture.teamMembers.native} />
          </Frame>
          <Frame label="agent server">
            <TeamMemberCard member={fixture.teamMembers.agentServer} />
          </Frame>
          <Frame label="persona (priced)">
            <TeamMemberCard member={fixture.teamMembers.persona} />
          </Frame>
          <Frame label="over budget">
            <TeamMemberCard member={fixture.teamMembers.overBudget} />
          </Frame>
        </div>
      </Block>

      <Block
        title="PersonaCard"
        note="Free and priced are the same layout with a different figure. Making the paid variant louder would turn a roster into a storefront, and the community tier is a first-class plugin surface (ADR-0019), not a lesser one."
      >
        <div className="grid gap-3 sm:grid-cols-3">
          <Frame label="free">
            <PersonaCard persona={fixture.personas.free} modelFamily="claude-sonnet-5" role="implementer" speed="balanced" />
          </Frame>
          <Frame label="priced">
            <PersonaCard persona={fixture.personas.priced} modelFamily="claude-sonnet-5" role="implementer" speed="deliberate" />
          </Frame>
          <Frame label="installed">
            <PersonaCard persona={fixture.personas.installed} modelFamily="claude-opus-5" role="reviewer" speed="quick" />
          </Frame>
        </div>
      </Block>

      <Block
        title="ServerCard"
        note="Capabilities carry their conformance result, because a server that claims df.vcs.open_pr and one that has demonstrated it are different things. `degraded` names the failing capability — that is the difference between a status and a diagnosis."
      >
        <div className="grid gap-3 lg:grid-cols-2">
          <Frame label="conformant">
            <ServerCard server={fixture.servers.conformant} />
          </Frame>
          <Frame label="degraded">
            <ServerCard server={fixture.servers.degraded} />
          </Frame>
          <Frame label="unreachable — neutral, not red">
            <ServerCard server={fixture.servers.unreachable} />
          </Frame>
          <Frame label="built-in connector, not yet authorized">
            <ServerCard server={fixture.servers.builtIn} onAuthorize={() => {}} />
          </Frame>
        </div>
      </Block>

      <Block
        title="MetricTile"
        note="The delta is coloured by whether it is good, not by its sign — cost rising and throughput rising are both 'up' and mean opposite things. With no `delta_is_good` the delta stays neutral rather than guessing."
      >
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <MetricTile label="Runs shipped" value={18} delta={0.22} delta_is_good series={[9, 11, 10, 14, 18]} period="7d" />
          <MetricTile label="Cost per run" value="$0.31" delta={-0.34} delta_is_good series={[0.62, 0.55, 0.48, 0.39, 0.31]} period="7d" />
          <MetricTile label="Conflicts caught" value={3} delta={0} series={[3, 3, 3, 3, 3]} period="7d" />
          <MetricTile label="Cache hit rate" value="" period="7d" />
        </div>
        <p className="text-2xs text-muted">
          up · down · flat · no-data. The fourth renders an em dash rather than 0 — a tile that
          shows zero when it means &ldquo;nothing reported&rdquo; teaches people to distrust the
          ones that mean zero.
        </p>
      </Block>

      <Block
        title="CostBar"
        note="Segments are sized by token share, not by dollars — the two disagree, because cached input is billed at a fraction of fresh input (ADR-0032). A segment that took the run over the line wears the over-budget token whatever its size."
      >
        <div className="grid gap-3 lg:grid-cols-2">
          <Frame label="default — by stage">
            <CostBar
              segments={[
                { label: "conversation", tokens: 786, usd: 0.0121 },
                { label: "plan", tokens: 1132, usd: 0.0118 },
                { label: "implement", tokens: 15879, usd: 0.1534 },
                { label: "verify", tokens: 0, usd: 0 },
              ]}
            />
          </Frame>
          <Frame label="over budget">
            <CostBar
              budget={200_000}
              segments={[
                { label: "plan", tokens: 12_400, usd: 0.24 },
                { label: "implement", tokens: 212_210, usd: 4.58, over_budget: true },
              ]}
            />
          </Frame>
        </div>
        <Frame label="with a ProvenancePopover on the cost figure">
          <div className="flex items-center gap-2">
            <span className="tnum text-sm text-primary">$0.1534</span>
            <ProvenancePopover provenance={fixture.provenance} />
            <span className="text-2xs text-muted">
              the same component answers &ldquo;why does this rule exist&rdquo; and &ldquo;why did
              this cost that&rdquo;
            </span>
          </div>
        </Frame>
      </Block>

      <Block
        title="CodeDiff"
        note="Spec ids in the gutter are the point: generated code carries a stable reference (ADR-0024), and this is where bidirectional traceability becomes readable rather than merely queryable."
      >
        <Frame label="default — with spec annotations">
          <CodeDiff
            patch={fixture.patch}
            filesChanged={["src/DarkFactory.Mcp/Endpoints/HealthEndpoints.cs"]}
            specAnnotations={{ 16: fixture.SPEC_ID, 17: fixture.SPEC_ID }}
          />
        </Frame>
        <Frame label="with a conflict marker">
          <CodeDiff patch={fixture.conflictPatch} filesChanged={["src/Billing/RenewalPolicy.cs"]} />
        </Frame>
      </Block>

      <Block
        title="DependencyGraph"
        note="Force-free on purpose. A force simulation re-lays-out every render, so the same neighbourhood looks different each time and nothing about the picture is memorable. Focus in the middle, neighbours on a stable ring. Real graph rendering is later work."
      >
        <Frame label="a spec neighbourhood, focused on the renewal rule">
          <DependencyGraph nodes={fixture.graph.nodes} edges={fixture.graph.edges} focus={fixture.graph.nodes[0].spec_id} />
        </Frame>
      </Block>

      <Block
        title="SyncStatus"
        note="`offline` is neutral, not amber. Local-first (ADR-0031) means losing the connection is a supported mode — colouring it as a warning would spend the same signal that `conflict`, which genuinely needs a person, has to use."
      >
        <Frame>
          <Row className="gap-4">
            <SyncStatus state="synced" />
            <SyncStatus state="syncing" pending={3} />
            <SyncStatus state="offline" pending={7} />
            <SyncStatus state="conflict" pending={1} />
          </Row>
          <Row className="mt-3 gap-4">
            {SYNC_STATES.map((state) => (
              <SyncStatus key={state} state={state} compact />
            ))}
          </Row>
        </Frame>
      </Block>

      <Block
        title="PayloadRenderer"
        note="Where ADR-0021 stops being a list in a document and becomes a contract: one fixed set of renderers, and a plugin either binds to a type here or proposes a new one through the convention process. There is deliberately no escape hatch."
      >
        <Frame label="one reply carrying three payloads">
          <PayloadRenderer payloads={fixture.multiPayloadReply} />
        </Frame>
        <div className="grid gap-3 lg:grid-cols-2">
          {EXAMPLES.map(({ label, payload }) => (
            <Frame key={label} label={label}>
              <PayloadRenderer payloads={[payload]} />
            </Frame>
          ))}
        </div>
        <Frame label="an unknown type — a named gap, never silence">
          <PayloadRenderer payloads={[{ type: "gantt_chart" } as unknown as Payload]} />
        </Frame>
      </Block>
    </Section>
  );
}

const EXAMPLES: { label: string; payload: Payload }[] = [
  {
    label: "markdown",
    payload: {
      type: "markdown",
      text: "Two things I deliberately did *not* fold in, because each would be its own node:\n\n- Status code semantics — 200-with-a-body versus 503.\n- Access control — whether the endpoint is public.",
    },
  },
  {
    label: "spec_diff",
    payload: {
      type: "spec_diff",
      amendment_id: "01M1SCBR4C1DV27DC1R8AHEBA0",
      summary: "Creates 1 node in the api layer.",
      diff: fixture.specDiff,
      conflicts: fixture.conflicts,
    },
  },
  {
    label: "stage_timeline",
    payload: {
      type: "stage_timeline",
      run_id: fixture.RUN_ID,
      snapshot_id: fixture.SNAPSHOT_ID,
      stages: fixture.timelineStages.running,
    },
  },
  {
    label: "approval_card",
    payload: { type: "approval_card", approval: fixture.approvals.awaiting },
  },
  {
    label: "dependency_graph",
    payload: {
      type: "dependency_graph",
      nodes: fixture.graph.nodes,
      edges: fixture.graph.edges,
      focus: fixture.graph.nodes[0].spec_id,
    },
  },
  {
    label: "code_diff",
    payload: {
      type: "code_diff",
      patch: fixture.patch,
      files_changed: ["src/DarkFactory.Mcp/Endpoints/HealthEndpoints.cs"],
      spec_annotations: { 16: fixture.SPEC_ID, 17: fixture.SPEC_ID },
    },
  },
  {
    label: "table",
    payload: {
      type: "table",
      caption: "Model calls for this run.",
      columns: [
        { key: "stage", label: "Stage" },
        { key: "model", label: "Model" },
        { key: "output", label: "Output", numeric: true },
        { key: "cost", label: "Cost", numeric: true },
      ],
      rows: [
        { stage: "conversation", model: "claude-opus-5", output: 701, cost: "$0.0121" },
        { stage: "plan", model: "claude-opus-5", output: 622, cost: "$0.0118" },
        { stage: "implement", model: "claude-sonnet-5", output: 15208, cost: "$0.1534" },
      ],
    },
  },
  {
    label: "form",
    payload: {
      type: "form",
      title: "Answer the implementer",
      submit_label: "Send",
      fields: [
        {
          name: "semantics",
          label: "Status code semantics",
          kind: "select",
          options: [
            { value: "200", label: "200 with a body reporting database state" },
            { value: "503", label: "503 when the database is unreachable" },
          ],
        },
        { name: "notes", label: "Anything else", kind: "textarea", help: "Optional." },
        { name: "public", label: "Publicly reachable", kind: "switch", value: false },
      ],
    },
  },
  {
    label: "metric",
    payload: {
      type: "metric",
      label: "Output tokens",
      value: 15208,
      delta: 2.29,
      delta_is_good: false,
      series: [4617, 5200, 8100, 11400, 15208],
      period: "vs last run",
    },
  },
];
