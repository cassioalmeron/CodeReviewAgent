import type {
  Analysis,
  AnalysisListItem,
  Diff,
  DiffListItem,
  JudgeEvaluation,
} from '@/types'

// Requests go to /api/* — proxied to the viewer API by Vite in dev (see vite.config.ts).
async function get<T>(path: string): Promise<T> {
  const res = await fetch(`/api${path}`)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText} — GET /api${path}`)
  return res.json() as Promise<T>
}

export const api = {
  diffs: () => get<DiffListItem[]>('/diffs'),
  diff: (id: number) => get<Diff>(`/diffs/${id}`),
  diffAnalyses: (id: number) => get<AnalysisListItem[]>(`/diffs/${id}/analyses`),

  analyses: () => get<AnalysisListItem[]>('/analyses'),
  analysis: (id: number) => get<Analysis>(`/analyses/${id}`),
  analysisEvaluations: (id: number) => get<JudgeEvaluation[]>(`/analyses/${id}/evaluations`),

  evaluations: () => get<JudgeEvaluation[]>('/evaluations'),
  evaluation: (id: number) => get<JudgeEvaluation>(`/evaluations/${id}`),
}
