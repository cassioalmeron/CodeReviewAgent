import { useMemo } from 'react'
import { api } from '@/services/api'
import { useProject } from '@/contexts/ProjectContext'
import { AssessmentsTable } from '@/components/features/AssessmentsTable'
import { Async } from '@/components/ui/Async'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { useAsync } from '@/hooks/useAsync'

export function AssessmentsPage() {
  const { projectId, project } = useProject()
  // Assessments don't carry a projectId; scope them to the project's reviews (cascade).
  const state = useAsync(async () => {
    const reviews = await api.reviews(projectId ?? undefined)
    const reviewIds = new Set(reviews.map((r) => r.id))
    const assessments = await api.assessments()
    return assessments.filter((a) => reviewIds.has(a.reviewId))
  }, [projectId])

  const sub = useMemo(
    () => `Every LLM assessment run in ${project?.name ?? 'this project'}. Open one for its findings and judge scores.`,
    [project],
  )

  return (
    <>
      <PageHeader title="Assessments" sub={sub} />
      <Async state={state}>
        {(rows) =>
          rows.length === 0 ? (
            <EmptyState title="No assessments stored yet" />
          ) : (
            <AssessmentsTable rows={rows} />
          )
        }
      </Async>
    </>
  )
}
