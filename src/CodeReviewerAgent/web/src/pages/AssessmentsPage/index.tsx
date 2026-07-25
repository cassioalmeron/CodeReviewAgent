import { api } from '@/services/api'
import { AnalysesTable } from '@/components/features/AnalysesTable'
import { Async } from '@/components/ui/Async'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { useAsync } from '@/hooks/useAsync'

export function AnalysesPage() {
  const state = useAsync(() => api.analyses(), [])
  return (
    <>
      <PageHeader
        title="Analyses"
        sub="Every LLM review the agent has run. Open one to read its findings and judge scores."
      />
      <Async state={state}>
        {(rows) =>
          rows.length === 0 ? (
            <EmptyState title="No analyses stored yet" />
          ) : (
            <AnalysesTable rows={rows} />
          )
        }
      </Async>
    </>
  )
}
