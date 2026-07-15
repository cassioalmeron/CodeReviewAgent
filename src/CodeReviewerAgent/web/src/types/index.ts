// Mirrors the C# records in CodeReviewerAgent.Core (serialized camelCase, enums as strings).

export type Severity = 'Info' | 'Warning' | 'Critical'

export type Category = 'Bug' | 'Security' | 'Performance' | 'Style' | 'Maintainability'

export interface Finding {
  file: string | null
  code_snippet: string | null
  severity: Severity | null
  category: Category | null
  problem: string | null
  suggestion: string | null
  line: number | null
}

/** GET /api/diffs — lightweight row (no Content). */
export interface DiffListItem {
  id: number
  source: string | null
  contentHash: string
  createdAt: string
  analysisCount: number
}

/** GET /api/diffs/{id} — full diff. */
export interface Diff {
  id: number
  content: string
  contentHash: string
  source: string | null
  createdAt: string
}

/** GET /api/analyses and /api/diffs/{id}/analyses — lightweight row (no Findings/Summary). */
export interface AnalysisListItem {
  id: number
  diffId: number
  engine: string | null
  model: string | null
  promptVersion: string | null
  cost: number
  latencyMs: number
  findingCount: number
  createdAt: string
}

/** GET /api/analyses/{id} — full analysis. */
export interface Analysis {
  id: number
  diffId: number
  summary: string | null
  findings: Finding[] | null
  engine: string | null
  model: string | null
  promptVersion: string | null
  cost: number
  latencyMs: number
  inputTokens: number
  outputTokens: number
  createdAt: string
}

/** GET /api/evaluations, /api/evaluations/{id}, /api/analyses/{id}/evaluations. */
export interface JudgeEvaluation {
  id: number
  analysisId: number
  rubricVersion: string | null
  judgeModel: string | null
  correctness: number
  actionability: number
  calibration: number
  signalToNoise: number
  overall: number
  rationale: string | null
  cost: number
  latencyMs: number
  inputTokens: number
  outputTokens: number
  createdAt: string
}
