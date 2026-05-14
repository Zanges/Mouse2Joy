// Tools exposed to each reviewer agent. Implementations are thin wrappers over
// the filesystem, git, and `gh`. The repo is checked out at the PR head SHA
// before the script runs, so reads see exactly the code the author proposed.
//
// All tool inputs that name a path are normalized and rejected if they would
// escape the repo root. We're running in CI with the workflow's GITHUB_TOKEN
// scope, but defense in depth is cheap.

import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, statSync, readdirSync } from "node:fs";
import * as path from "node:path";

const REPO_ROOT = process.cwd().endsWith(path.join(".github", "claude-review"))
  ? path.resolve(process.cwd(), "..", "..")
  : process.cwd();

function safeResolve(rel: string): string {
  // Forbid absolute paths and `..` traversal once normalized.
  const joined = path.resolve(REPO_ROOT, rel);
  const relFromRoot = path.relative(REPO_ROOT, joined);
  if (relFromRoot.startsWith("..") || path.isAbsolute(relFromRoot)) {
    throw new Error(`path escapes repo root: ${rel}`);
  }
  return joined;
}

function runCapture(cmd: string, args: string[], opts: { cwd?: string; input?: string } = {}): { stdout: string; stderr: string; status: number } {
  const result = spawnSync(cmd, args, {
    cwd: opts.cwd ?? REPO_ROOT,
    input: opts.input,
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024,
  });
  return {
    stdout: result.stdout ?? "",
    stderr: result.stderr ?? "",
    status: result.status ?? 1,
  };
}

// ---------- get_file_diff ----------

export function getFileDiff(args: { path: string }): string {
  const target = args.path;
  safeResolve(target); // validates only -- we pass the relative form to gh.

  const prNumber = process.env.PR_NUMBER;
  const repo = process.env.REPO;
  if (!prNumber || !repo) {
    return "error: PR_NUMBER or REPO not set";
  }

  const res = runCapture("gh", ["pr", "diff", prNumber, "--repo", repo, "--", target]);
  if (res.status !== 0) {
    return `error: gh pr diff failed: ${res.stderr.trim()}`;
  }
  const out = res.stdout.trim();
  if (out.length === 0) {
    return `(no diff for ${target} -- file may be unchanged or only renamed)`;
  }
  // Cap individual diff output so a 20k-line file rewrite can't blow the context.
  return truncate(out, 40_000, `[diff truncated; original size ${out.length} chars]`);
}

// ---------- read_file ----------

export function readFile(args: { path: string; start_line?: number; end_line?: number }): string {
  const abs = safeResolve(args.path);
  if (!existsSync(abs)) return `error: file does not exist: ${args.path}`;
  if (!statSync(abs).isFile()) return `error: not a regular file: ${args.path}`;

  const raw = readFileSync(abs, "utf8");
  const lines = raw.split(/\r?\n/);
  const start = Math.max(1, args.start_line ?? 1);
  const end = Math.min(lines.length, args.end_line ?? lines.length);
  if (start > end) return `error: start_line ${start} > end_line ${end}`;

  const slice = lines.slice(start - 1, end);
  const numbered = slice.map((line, idx) => `${start + idx}\t${line}`).join("\n");
  return truncate(numbered, 40_000, `[file truncated; ${lines.length} total lines, showing ${start}-${end}]`);
}

// ---------- grep_repo ----------

export function grepRepo(args: { pattern: string; glob?: string; max_results?: number }): string {
  const max = Math.min(args.max_results ?? 200, 500);
  const cmdArgs = ["grep", "-nI", "--no-color", "-E", args.pattern];
  if (args.glob) {
    cmdArgs.push("--", args.glob);
  }
  const res = runCapture("git", cmdArgs);
  if (res.status === 1 && res.stdout.length === 0) {
    return `(no matches for /${args.pattern}/${args.glob ? " in " + args.glob : ""})`;
  }
  if (res.status !== 0 && res.status !== 1) {
    return `error: git grep failed: ${res.stderr.trim()}`;
  }
  const lines = res.stdout.split("\n").filter(Boolean);
  const capped = lines.slice(0, max).join("\n");
  const suffix = lines.length > max ? `\n[results capped at ${max} of ${lines.length}]` : "";
  return truncate(capped + suffix, 40_000, `[grep output truncated]`);
}

// ---------- list_dir ----------

export function listDir(args: { path: string }): string {
  const abs = safeResolve(args.path);
  if (!existsSync(abs)) return `error: path does not exist: ${args.path}`;
  if (!statSync(abs).isDirectory()) return `error: not a directory: ${args.path}`;

  const entries = readdirSync(abs, { withFileTypes: true });
  const formatted = entries
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(e => (e.isDirectory() ? `${e.name}/` : e.name))
    .join("\n");
  return truncate(formatted, 20_000, `[listing truncated; ${entries.length} entries]`);
}

// ---------- shared ----------

function truncate(s: string, maxLen: number, suffix: string): string {
  if (s.length <= maxLen) return s;
  return s.slice(0, maxLen) + "\n" + suffix;
}

// ---------- tool schemas for Anthropic SDK ----------

export const TOOL_SCHEMAS = [
  {
    name: "get_file_diff",
    description:
      "Get the unified diff for one changed file in the PR. Use this when you need to see exactly what changed in a file. Returns an empty/synthetic message for unchanged or renamed-only files.",
    input_schema: {
      type: "object" as const,
      properties: {
        path: { type: "string", description: "Repo-relative path to a file in the PR diff." },
      },
      required: ["path"],
    },
  },
  {
    name: "read_file",
    description:
      "Read a slice (or all) of a file from the PR head. Use this to see context around a diff hunk, look at callers of a changed symbol, or read related files (tests, ai-docs/, CLAUDE.md). Line numbers are returned as a TAB prefix.",
    input_schema: {
      type: "object" as const,
      properties: {
        path: { type: "string", description: "Repo-relative path." },
        start_line: { type: "number", description: "1-indexed start line (inclusive). Defaults to 1." },
        end_line: { type: "number", description: "1-indexed end line (inclusive). Defaults to end of file." },
      },
      required: ["path"],
    },
  },
  {
    name: "grep_repo",
    description:
      "Search the repo for a regex pattern (POSIX extended regex). Optionally restrict to a glob (e.g. 'src/Mouse2Joy.Engine/**/*.cs'). Returns up to max_results 'file:line:content' matches. Useful for finding callers, similar patterns, or near-duplicates.",
    input_schema: {
      type: "object" as const,
      properties: {
        pattern: { type: "string", description: "Extended regex pattern." },
        glob: { type: "string", description: "Optional pathspec to limit the search (e.g. ':(glob)src/**/*.cs')." },
        max_results: { type: "number", description: "Max matches to return (default 200, hard cap 500)." },
      },
      required: ["pattern"],
    },
  },
  {
    name: "list_dir",
    description:
      "List the entries of a directory in the repo. Directories are suffixed with '/'.",
    input_schema: {
      type: "object" as const,
      properties: {
        path: { type: "string", description: "Repo-relative directory path." },
      },
      required: ["path"],
    },
  },
] as const;

export type ToolName = (typeof TOOL_SCHEMAS)[number]["name"];

export function dispatchTool(name: string, input: unknown): string {
  try {
    switch (name) {
      case "get_file_diff":
        return getFileDiff(input as { path: string });
      case "read_file":
        return readFile(input as { path: string; start_line?: number; end_line?: number });
      case "grep_repo":
        return grepRepo(input as { pattern: string; glob?: string; max_results?: number });
      case "list_dir":
        return listDir(input as { path: string });
      default:
        return `error: unknown tool '${name}'`;
    }
  } catch (err) {
    return `error: ${(err as Error).message}`;
  }
}
