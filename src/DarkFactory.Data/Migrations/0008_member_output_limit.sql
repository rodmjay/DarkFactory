-- The largest answer a team member may produce in one call.
--
-- Per member because the roles differ by an order of magnitude. The 3d
-- acceptance run made the case: the implementer, held to the gateway's
-- 8192-token default, returned whole files and was cut off mid-JSON. That
-- presented as "the response was not the required JSON object" — a
-- malformed answer — when it was a configuration mistake, and it cost a
-- full retry and 87 seconds to discover.
ALTER TABLE team_members ADD COLUMN max_output_tokens integer;
