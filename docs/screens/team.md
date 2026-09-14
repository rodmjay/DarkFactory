# Screen: Team
Route: /team
Reads (local SQLite): team_members, skills, skill_revisions, model_calls (spend)
Dispatches: df.team.raise_budget
Renders:
  team_member            → TeamMemberCard (states: native agent, agent server, persona, over budget)
  assignment map         → Table (stage or hook, member, deployment, why)
  marketplace listing    → PersonaCard (states: free, priced, installed, on this team)
States covered: within budget, over budget with a raise-the-cap action, no cap, human hook owner
Proposed additions: none — `PersonaCard.onTeam` was added to packages/ui, the showcase and the manifest in this commit before the screen used it.

## Notes

Every card states the model family it runs on (ADR-0028). "Which model am I
buying" is the question a price makes people ask, and a persona that hides
it is selling a name rather than a capability.

`installed` and `on this team` are separate states. A persona can be bought
for the org and used by nobody, and collapsing the two makes "why is this
not running my work" unanswerable from the card. That is the one component
state this screen needed that the showcase did not have, so it was added
there first.

The assignment map's last column is the point of the table: verifiability
decides who owns a stage, not seniority. Implement can run on a cheaper
model because tests check the output; verify cannot, because judging
completion is the failure nobody notices.
