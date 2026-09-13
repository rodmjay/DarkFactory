using System.Text;
using DarkFactory.Contracts;
using DarkFactory.Core;

namespace DarkFactory.Data;

/// <summary>
/// The extraction prompt for corpus intake (docs/adr/0037).
///
/// Split like the architect's, and for the same reason, but the split falls
/// somewhere more useful: the whole corpus goes in the cacheable half. Every
/// extraction in an intake sees every document — which is what lets it
/// settle a detail from another document instead of asking a person about
/// it — and pays for that once, not once per document.
/// </summary>
public static class IntakePrompt
{
    /// <summary>Bumped when the prompt changes, so docs/adr/0032 can attribute outcomes to a template.</summary>
    public const string TemplateVersion = "intake/1";

    public static ContextSkill IntakeSkill { get; } = new(
        Name: "intake",
        Version: "0.1.0",
        Instructions: """
            You are importing an existing body of specifications into a project's
            specification graph. The corpus below is every source document, written as
            prose before the graph existed. You work on ONE target document at a time.

            1. Extract. Restate what the target document specifies as small spec nodes:
               one behaviour, rule, constraint, decision, entity or interface per node,
               stated so it can be checked. Keep the source's meaning exactly. Do not
               invent requirements, numbers or names that the corpus does not contain —
               a plausible detail nobody wrote down is a hole, not a node.

            2. Find the holes. A hole is anything an implementer would have to guess at.
               Before reporting one, look across the whole corpus: if another document
               settles it, it is not a hole — use that answer and cite the document in
               the node's rationale. Ask each remaining hole as one direct question a
               product owner can answer in a sentence or two. Prefer a few consequential
               questions to many trivial ones; an empty list is a legitimate answer.

            3. Stay in scope. Extract what the target document states. Where it only
               restates something another document owns in more detail, leave it to that
               document's extraction. Relate to other documents by citing them in a
               rationale; edges may only join nodes within this draft.

            Design notes, file layouts and implementation sketches in a source are not
            specifications. Extract the behaviour they imply only where the document
            states it as a requirement; otherwise leave them out.
            """);

    /// <summary>
    /// Everything identical across an intake's extractions: skill, standards,
    /// the response contract, and the corpus. Sent with a cache breakpoint
    /// after it (docs/adr/0032).
    /// </summary>
    public static string CacheablePrefix(IReadOnlyList<IntakeSource> corpus)
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are the intake agent for a software project maintained by Dark Factory.");
        builder.AppendLine();
        builder.AppendLine($"## Skill: {IntakeSkill.Name} v{IntakeSkill.Version}");
        builder.AppendLine(IntakeSkill.Instructions);
        builder.AppendLine();

        var standards = ArchitectPrompt.DefaultStandards;
        builder.AppendLine($"## Standards ({standards.Layer}, from {standards.SourceRef})");
        builder.AppendLine(standards.Text);
        builder.AppendLine();

        builder.AppendLine("## How to answer");
        builder.AppendLine();
        builder.AppendLine("Reply with a single JSON object and nothing else. No prose before or after, no code fence.");
        builder.AppendLine();
        builder.AppendLine("""
            {
              "reply": "two or three sentences for the reviewer: what you extracted, and the holes that matter most",
              "draft": { "creates": [], "revises": [], "retires": [], "edge_adds": [], "edge_retires": [] },
              "holes": [
                { "id": null, "kind": "ambiguity", "question": "…", "quote": "…", "affects": [0] }
              ]
            }
            """);
        builder.AppendLine();
        builder.AppendLine("\"draft\" must match this schema exactly:");
        builder.AppendLine();
        builder.AppendLine(SpecDiffSchema.SchemaText);
        builder.AppendLine();
        builder.AppendLine("""
            Rules for the draft:
            - Only "creates" and "edge_adds" may be non-empty. "revises", "retires" and
              "edge_retires" must be present and empty: an intake adds to the graph, it never
              changes what is already there.
            - Every array must be present, even when empty.
            - You do not assign spec ids. Join nodes within this draft with "new:N", where N
              is the node's zero-based index in "creates".
            - Every node's "rationale" names the section of the target document it came from,
              quoting the phrase where that helps. Where another document settled a detail,
              cite that document too.
            - Layers are lower_snake_case names for the parts of the product the corpus
              describes. Reuse the layers already in use (listed with the target) and add
              a new one only when none fits.

            Rules for holes:
            - "kind" is exactly one of: ambiguity (more than one reading, built differently),
              contradiction (with another document, named, or with itself), untestable (no
              observable condition shows it is met), undefined_term (behaviour depends on a
              term no document defines), source_open_question (the document says it is
              undecided), missing (behaviour it plainly needs that nothing specifies).
            - "question" is one direct question. "quote" is the short passage it is about.
            - "affects" lists indices into draft.creates whose wording depends on the answer;
              [] when the hole is about something not drafted.
            - "id" is null for a new hole, or the id of a question listed as [open] with the
              target that is still a hole. Never return the id of an answered or deferred
              question.
            """);
        builder.AppendLine();

        builder.AppendLine($"## The corpus ({corpus.Count} documents)");
        builder.AppendLine();
        foreach (var source in corpus)
        {
            builder.AppendLine($"=== {source.SourceRef} — {source.Title} ===");
            builder.AppendLine(source.Content.Trim());
            builder.AppendLine($"=== end {source.SourceRef} ===");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>The half that changes per extraction: which document, and what has been asked about it.</summary>
    public static string SystemPrompt(
        IntakeSource target,
        int revision,
        IReadOnlyList<IntakeQuestion> questions,
        string? previousDraft,
        IReadOnlyList<string> layersInUse)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Target document");
        builder.AppendLine($"`{target.SourceRef}` — {target.Title}. This is extraction {revision} of it.");
        builder.AppendLine();

        builder.AppendLine("## Layers already in use");
        builder.AppendLine(layersInUse.Count == 0
            ? "(none yet — this is the first document drafted)"
            : string.Join(", ", layersInUse));
        builder.AppendLine();

        builder.AppendLine("## Questions already raised about this document");
        var relevant = questions.Where(q => q.Status != IntakeQuestionStatus.Resolved).ToList();
        if (relevant.Count == 0)
        {
            builder.AppendLine("(none yet)");
        }
        foreach (var question in relevant)
        {
            switch (question.Status)
            {
                case IntakeQuestionStatus.Answered:
                    builder.AppendLine($"- [answered] `{question.Id}` ({question.Kind}) {question.Question}");
                    builder.AppendLine($"  Answer: {question.Answer}");
                    builder.AppendLine("  Build this answer into the draft. Do not ask it again.");
                    break;
                case IntakeQuestionStatus.Deferred:
                    builder.AppendLine($"- [deferred] `{question.Id}` ({question.Kind}) {question.Question}");
                    builder.AppendLine($"  Left open deliberately: {question.Answer}");
                    builder.AppendLine("  Do not ask it again, and do not guess at it in the draft either.");
                    break;
                default:
                    builder.AppendLine($"- [open] `{question.Id}` ({question.Kind}) {question.Question}");
                    builder.AppendLine("  Still a hole? Return it in \"holes\" with this id. Settled by the corpus or an answer above? Leave it out.");
                    break;
            }
        }
        builder.AppendLine();

        if (previousDraft is not null)
        {
            builder.AppendLine("## Your previous draft of this document");
            builder.AppendLine("Revise it rather than starting over, and keep a node's wording unchanged where nothing about it changed.");
            builder.AppendLine(previousDraft);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string Instruction(IntakeSource target) =>
        $"Extract `{target.SourceRef}` now. Reply with the single JSON object only.";

    public static string RetryMessage(SchemaValidationResult validation) =>
        $"""
         Your previous response could not be accepted. These are the problems with it:
         {validation.AsBulletList()}

         Reply again with a single JSON object in the required format, correcting every problem listed.
         """;
}
