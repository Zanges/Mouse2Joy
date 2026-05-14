// Generic agentic-loop driver. Runs one reviewer agent to completion.
//
// Each agent gets:
//   - A reviewer-specific system prompt (role + scope + output schema).
//   - A shared CLAUDE.md block in the system messages, with cache_control set
//     so the prefix hits the prompt cache across agents.
//   - A user message with the PR context (title, list of changed files).
//   - The four tools defined in tools.ts.
//
// The loop runs until the model emits an assistant message with no tool_use
// blocks; at that point we parse the final text as JSON matching FindingsZ.

import Anthropic from "@anthropic-ai/sdk";
import { z } from "zod";
import { TOOL_SCHEMAS, dispatchTool } from "./tools.js";

export const FindingZ = z.object({
  path: z.string(),
  line_start: z.number().int().positive(),
  line_end: z.number().int().positive(),
  severity: z.enum(["critical", "suggestion", "nit"]),
  message: z.string().min(1),
});
export type Finding = z.infer<typeof FindingZ>;

export const ReviewResultZ = z.object({
  summary: z.string(),
  findings: z.array(FindingZ),
});
export type ReviewResult = z.infer<typeof ReviewResultZ>;

// JSON Schema mirror of ReviewResultZ, fed to output_config.format so the
// API itself constrains Claude's final response. zod still validates post-hoc
// as a belt-and-braces guard (different layer; structured outputs has its
// own subset of JSON Schema it accepts).
const REVIEW_RESULT_JSON_SCHEMA = {
  type: "object",
  additionalProperties: false,
  properties: {
    summary: { type: "string" },
    findings: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        properties: {
          path: { type: "string" },
          line_start: { type: "integer" },
          line_end: { type: "integer" },
          severity: {
            type: "string",
            enum: ["critical", "suggestion", "nit"],
          },
          message: { type: "string" },
        },
        required: ["path", "line_start", "line_end", "severity", "message"],
      },
    },
  },
  required: ["summary", "findings"],
} as const;

export interface RunAgentArgs {
  client: Anthropic;
  model: string;
  angleId: string;            // e.g. "security" -- used in logs
  reviewerSystemPrompt: string;
  claudeMd: string;
  userPrompt: string;
  maxTurns?: number;
}

export interface AgentRunOutcome {
  angleId: string;
  result: ReviewResult | null;
  rawFinal: string;
  error?: string;
  inputTokens: number;
  outputTokens: number;
  cacheCreationTokens: number;
  cacheReadTokens: number;
  turns: number;
}

export async function runAgent(args: RunAgentArgs): Promise<AgentRunOutcome> {
  const maxTurns = args.maxTurns ?? 20;
  const messages: Anthropic.MessageParam[] = [
    { role: "user", content: args.userPrompt },
  ];

  const system: Anthropic.TextBlockParam[] = [
    { type: "text", text: args.reviewerSystemPrompt },
    {
      type: "text",
      text: `# Project conventions (CLAUDE.md)\n\n${args.claudeMd}`,
      cache_control: { type: "ephemeral" },
    },
  ];

  let inputTokens = 0;
  let outputTokens = 0;
  let cacheCreationTokens = 0;
  let cacheReadTokens = 0;
  let turns = 0;
  let lastText = "";

  while (turns < maxTurns) {
    turns += 1;
    let response: Anthropic.Message;
    try {
      response = await args.client.messages.create({
        model: args.model,
        max_tokens: 16000,
        // Adaptive thinking lets Opus 4.7 decide how much to think per turn.
        // effort: "high" raises the cost-quality bar -- this pipeline only
        // fires on substantive PRs (gated upstream) and we want thorough
        // analysis, not the cheapest possible run.
        thinking: { type: "adaptive" },
        output_config: {
          effort: "high",
          // Constrains the final assistant message to match the review-result
          // schema. The loop terminates when no tool_use blocks are emitted,
          // which is when this format takes effect.
          format: {
            type: "json_schema",
            schema: REVIEW_RESULT_JSON_SCHEMA,
          },
        },
        system,
        tools: TOOL_SCHEMAS as unknown as Anthropic.ToolUnion[],
        messages,
      });
    } catch (err) {
      return {
        angleId: args.angleId,
        result: null,
        rawFinal: "",
        error: `API error on turn ${turns}: ${(err as Error).message}`,
        inputTokens,
        outputTokens,
        cacheCreationTokens,
        cacheReadTokens,
        turns,
      };
    }

    inputTokens += response.usage.input_tokens;
    outputTokens += response.usage.output_tokens;
    cacheCreationTokens += response.usage.cache_creation_input_tokens ?? 0;
    cacheReadTokens += response.usage.cache_read_input_tokens ?? 0;

    // Append the assistant message verbatim so subsequent turns see the
    // full conversation including tool_use blocks.
    messages.push({ role: "assistant", content: response.content });

    const toolUses = response.content.filter(
      (b): b is Anthropic.ToolUseBlock => b.type === "tool_use",
    );

    if (toolUses.length === 0) {
      // Final turn -- collect text and stop.
      const textBlocks = response.content.filter(
        (b): b is Anthropic.TextBlock => b.type === "text",
      );
      lastText = textBlocks.map(b => b.text).join("\n").trim();
      break;
    }

    const toolResults: Anthropic.ToolResultBlockParam[] = toolUses.map(tu => ({
      type: "tool_result",
      tool_use_id: tu.id,
      content: dispatchTool(tu.name, tu.input),
    }));
    messages.push({ role: "user", content: toolResults });
  }

  if (lastText === "") {
    return {
      angleId: args.angleId,
      result: null,
      rawFinal: "",
      error: `agent did not produce a final text response within ${maxTurns} turns`,
      inputTokens,
      outputTokens,
      cacheCreationTokens,
      cacheReadTokens,
      turns,
    };
  }

  // Extract a JSON object from the final text. Accept either a bare JSON
  // object or one wrapped in a ```json ... ``` fence.
  const json = extractJsonObject(lastText);
  if (!json) {
    return {
      angleId: args.angleId,
      result: null,
      rawFinal: lastText,
      error: "no JSON object found in final response",
      inputTokens,
      outputTokens,
      cacheCreationTokens,
      cacheReadTokens,
      turns,
    };
  }

  const parsed = ReviewResultZ.safeParse(json);
  if (!parsed.success) {
    return {
      angleId: args.angleId,
      result: null,
      rawFinal: lastText,
      error: `JSON did not match schema: ${parsed.error.message}`,
      inputTokens,
      outputTokens,
      cacheCreationTokens,
      cacheReadTokens,
      turns,
    };
  }

  return {
    angleId: args.angleId,
    result: parsed.data,
    rawFinal: lastText,
    inputTokens,
    outputTokens,
    cacheCreationTokens,
    cacheReadTokens,
    turns,
  };
}

function extractJsonObject(text: string): unknown | null {
  const fence = text.match(/```(?:json)?\s*([\s\S]*?)```/);
  const candidate = fence ? fence[1]!.trim() : text.trim();

  // Find the first '{' and the matching closing brace via brace counting,
  // ignoring braces inside strings.
  const start = candidate.indexOf("{");
  if (start === -1) return null;
  let depth = 0;
  let inString = false;
  let escape = false;
  for (let i = start; i < candidate.length; i++) {
    const ch = candidate[i]!;
    if (escape) { escape = false; continue; }
    if (inString) {
      if (ch === "\\") escape = true;
      else if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') inString = true;
    else if (ch === "{") depth += 1;
    else if (ch === "}") {
      depth -= 1;
      if (depth === 0) {
        const slice = candidate.slice(start, i + 1);
        try { return JSON.parse(slice); }
        catch { return null; }
      }
    }
  }
  return null;
}
