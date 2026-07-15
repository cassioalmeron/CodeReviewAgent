import { api } from '@/services/api'
import { Async } from '@/components/ui/Async'
import { EvaluationsTable } from '@/components/features/EvaluationsTable'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { useAsync } from '@/hooks/useAsync'

export function EvaluationsPage() {
  const state = useAsync(() => api.evaluations(), [])
  return (
    <>
      <PageHeader
        title="Evaluations"
        sub="The judge's quality scores for stored analyses. Open one for the full rubric breakdown."
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
