// Mirrors the C# records in CodeReviewerAgent.Core / the Api DTOs (serialized camelCase, enums as strings).

export type Severity = 'Info' | 'Warning' | 'Critical'

export type Category = 'Bug' | 'Security' | 'Performance' | 'Style' | 'Maintainability' | 'Convention'

export interface Finding {
  file: string | null
  code_snippet: string | null
  severity: Severity | null
  category: Category | null
  problem: string | null
  suggestion: string | null
  line: number | null
}

/** GET /api/projects — lightweight row with per-project counts. */
export interface ProjectListItem {
  id: number
  name: string
  folder: string
  createdAt: string
  reviewCount: number
  assessmentCount: number
  lastAssessmentAt: string | null
}

/** GET /api/projects/{id}. */
export interface Project {
  id: number
  name: string
  folder: string
  createdAt: string
}

/** One slice of a pie / a labelled count. */
export interface SliceCount {
  label: string
  count: number
}

/** One assessment as a point on the cost/tokens/latency chart. */
export interface AssessmentRunPoint {
  assessmentId: number
  createdAt: string
  cost: number
  inputTokens: number
  outputTokens: number
  latencyMs: number
  findingCount: number
}

/** Averaged judge rubric scores for a project. */
export interface JudgeAverages {
  correctness: number
  actionability: number
  calibration: number
  signalToNoise: number
  overall: number
  evaluationCount: number
}

/** GET /api/projects/{id}/stats — the whole dashboard in one payload. */
export interface ProjectStats {
  projectId: number
  reviewCount: number
  assessmentCount: number
  evaluationCount: number
  findingCount: number
  totalCost: number
  totalInputTokens: number
  totalOutputTokens: number
  avgLatencyMs: number
  bySeverity: SliceCount[]
  byCategory: SliceCount[]
  topFiles: SliceCount[]
  runs: AssessmentRunPoint[]
  judge: JudgeAverages | null
}

/** GET /api/reviews and /api/projects/{id}/reviews — lightweight row (no Content). */
export interface ReviewListItem {
  id: number
  projectId: number
  projectName: string | null
  source: string | null
  contentHash: string
  createdAt: string
  assessmentCount: number
}

/** GET /api/reviews/{id} — full review (captured diff). */
export interface Review {
  id: number
  projectId: number
  content: string
  contentHash: string
  source: string | null
  createdAt: string
}

/** GET /api/assessments and /api/reviews/{id}/assessments — lightweight row (no Findings/Summary). */
export interface AssessmentListItem {
  id: number
  reviewId: number
  engine: string | null
  model: string | null
  promptVersion: string | null
  cost: number
  latencyMs: number
  findingCount: number
  createdAt: string
}

/** GET /api/assessments/{id} — full assessment. */
export interface Assessment {
  id: number
  reviewId: number
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

/** GET /api/evaluations, /api/evaluations/{id}, /api/assessments/{id}/evaluations. */
export interface Evaluation {
  id: number
  assessmentId: number
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
