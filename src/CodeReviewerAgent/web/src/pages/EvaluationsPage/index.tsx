import { api } from '@/services/api'
import { useProject } from '@/contexts/ProjectContext'
import { Async } from '@/components/ui/Async'
import { EvaluationsTable } from '@/components/features/EvaluationsTable'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { useAsync } from '@/hooks/useAsync'

export function EvaluationsPage() {
  const { projectId, project } = useProject()
  // Evaluations cascade to a project via assessment → review; scope them the same way.
  const state = useAsync(async () => {
    const reviews = await api.reviews(projectId ?? undefined)
    const reviewIds = new Set(reviews.map((r) => r.id))
    const assessments = await api.assessments()
    const assessmentIds = new Set(
      assessments.filter((a) => reviewIds.has(a.reviewId)).map((a) => a.id),
    )
    const evaluations = await api.evaluations()
    return evaluations.filter((e) => assessmentIds.has(e.assessmentId))
  }, [projectId])

  return (
    <>
      <PageHeader
        title="Evaluations"
        sub={`The judge's quality scores in ${project?.name ?? 'this project'}. Open one for the full rubric breakdown.`}
      />
      <Async state={state}>
        {(rows) =>
          rows.length === 0 ? (
            <EmptyState title="No evaluations stored yet" />
          ) : (
            <EvaluationsTable rows={rows} />
          )
        }
      </Async>
    </>
  )
}
