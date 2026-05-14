// Merge per-agent results into a single GitHub PR review.
//
// Output shape matches POST /repos/{owner}/{repo}/pulls/{n}/reviews:
//   { commit_id, event: "COMMENT", body, comments: [{path, line, side, body}] }
//
// We expose the merged structure; the caller posts it via gh.

import type { AgentRunOutcome } from "./agent.js";

export interface ReviewPayload {
  commit_id: string;
  event: "COMMENT";
  body: string;
  comments: Array<{
    path: string;
    line: number;
    side: "RIGHT";
    body: string;
  }>;
}

interface AggregateArgs {
  outcomes: AgentRunOutcome[];
  commitId: string;
  prTitle: string;
  angleLabels: Record<string, string>;     // angleId -> display name (e.g. "security" -> "Security")
  changedFilePaths: Set<string>;            // restrict inline comments to changed files
}

export function aggregate(args: AggregateArgs): ReviewPayload {
  const sections: string[] = [];
  const comments: ReviewPayload["comments"] = [];
  const errored: string[] = [];

  // Stable, predictable section order.
  const ordered = [...args.outcomes].sort((a, b) =>
    a.angleId.localeCompare(b.angleId),
  );

  for (const o of ordered) {
    const label = args.angleLabels[o.angleId] ?? o.angleId;
    if (o.error || !o.result) {
      errored.push(`- **${label}**: ${o.error ?? "no result"}`);
      continue;
    }

    const critical = o.result.findings.filter(f => f.severity === "critical");
    const sectionParts: string[] = [`## ${label}`, "", o.result.summary.trim() || "_(no summary)_"];
    if (critical.length > 0) {
      sectionParts.push("", "**Critical findings:**");
      for (const f of critical) {
        const lineRef = f.line_start === f.line_end ? `L${f.line_start}` : `L${f.line_start}-${f.line_end}`;
        sectionParts.push(`- \`${f.path}\` ${lineRef} — ${oneLine(f.message)}`);
      }
    }
    sections.push(sectionParts.join("\n"));

    // Inline comments: every finding the agent produced, anchored to a
    // changed file. GitHub rejects review comments on lines not in the PR
    // diff, so we drop findings whose path isn't in the diff -- those get
    // surfaced in the critical-bullets list above instead.
    for (const f of o.result.findings) {
      if (!args.changedFilePaths.has(f.path)) continue;
      const sev = severityBadge(f.severity);
      const body = `**[${label} – ${sev}]** ${f.message.trim()}`;
      comments.push({
        path: f.path,
        line: clampPositive(f.line_end),
        side: "RIGHT",
        body,
      });
    }
  }

  const header = [
    `# Claude multi-agent review`,
    "",
    `_Eight reviewer agents looked at this PR from different angles. Findings are advisory; nothing blocks merge._`,
    "",
    `_PR: ${escapeMd(args.prTitle)} @ \`${args.commitId.slice(0, 7)}\`_`,
  ].join("\n");

  const usage = renderUsage(args.outcomes);
  const errorBlock =
    errored.length > 0
      ? `\n\n## Agents that errored\n\n${errored.join("\n")}`
      : "";

  const body = `${header}\n\n${sections.join("\n\n")}${errorBlock}\n\n---\n${usage}`;

  return {
    commit_id: args.commitId,
    event: "COMMENT",
    body,
    comments,
  };
}

function severityBadge(s: "critical" | "suggestion" | "nit"): string {
  return s;
}

function oneLine(s: string): string {
  return s.replace(/\s+/g, " ").trim();
}

function clampPositive(n: number): number {
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : 1;
}

function escapeMd(s: string): string {
  return s.replace(/[<>]/g, " ").replace(/\s+/g, " ").trim();
}

function renderUsage(outcomes: AgentRunOutcome[]): string {
  const totals = outcomes.reduce(
    (acc, o) => ({
      input: acc.input + o.inputTokens,
      output: acc.output + o.outputTokens,
      cacheCreation: acc.cacheCreation + o.cacheCreationTokens,
      cacheRead: acc.cacheRead + o.cacheReadTokens,
      turns: acc.turns + o.turns,
    }),
    { input: 0, output: 0, cacheCreation: 0, cacheRead: 0, turns: 0 },
  );
  return `<sub>Token usage — input: ${fmt(totals.input)}, output: ${fmt(totals.output)}, cache-write: ${fmt(totals.cacheCreation)}, cache-read: ${fmt(totals.cacheRead)}, total turns: ${totals.turns}.</sub>`;
}

function fmt(n: number): string {
  return n.toLocaleString("en-US");
}
