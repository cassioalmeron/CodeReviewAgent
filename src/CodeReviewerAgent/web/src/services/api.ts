import type {
  Assessment,
  AssessmentListItem,
  Evaluation,
  Project,
  ProjectListItem,
  ProjectStats,
  Review,
  ReviewListItem,
} from '@/types'

// Requests go to /api/* — proxied to the viewer API by Vite in dev (see vite.config.ts).
async function get<T>(path: string): Promise<T> {
  const res = await fetch(`/api${path}`)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText} — GET /api${path}`)
  return res.json() as Promise<T>
}

function query(projectId?: number): string {
  return projectId == null ? '' : `?projectId=${projectId}`
}

export const api = {
  projects: () => get<ProjectListItem[]>('/projects'),
  project: (id: number) => get<Project>(`/projects/${id}`),
  projectStats: (id: number) => get<ProjectStats>(`/projects/${id}/stats`),
  projectReviews: (id: number) => get<ReviewListItem[]>(`/projects/${id}/reviews`),

  reviews: (projectId?: number) => get<ReviewListItem[]>(`/reviews${query(projectId)}`),
  review: (id: number) => get<Review>(`/reviews/${id}`),
  reviewAssessments: (id: number) => get<AssessmentListItem[]>(`/reviews/${id}/assessments`),

  assessments: () => get<AssessmentListItem[]>('/assessments'),
  assessment: (id: number) => get<Assessment>(`/assessments/${id}`),
  assessmentEvaluations: (id: number) => get<Evaluation[]>(`/assessments/${id}/evaluations`),

  evaluations: () => get<Evaluation[]>('/evaluations'),
  evaluation: (id: number) => get<Evaluation>(`/evaluations/${id}`),
}
