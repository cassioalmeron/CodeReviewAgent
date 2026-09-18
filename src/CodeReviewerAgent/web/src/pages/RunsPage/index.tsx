import { api } from '@/services/api'
import { Async } from '@/components/ui/Async'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { VerdictTag } from '@/components/ui/VerdictTag'
import { useAsync } from '@/hooks/useAsync'
import type { GoldenRunListItem } from '@/types'
import { cost, dateTime, duration, latency, tokens } from '@/utils/format'

const columns: Column<GoldenRunListItem>[] = [
  { header: 'Model', render: (r) => r.model },
  { header: 'Skills', render: (r) => r.skills ?? '—' },
  { header: 'Started', render: (r) => dateTime(r.startedAt) },
  { header: 'Duration', render: (r) => duration(r.durationMs), align: 'right' },
  { header: 'Cost', render: (r) => cost(r.cost), align: 'right' },
  { header: 'Tokens', render: (r) => tokens(r.inputTokens + r.outputTokens), align: 'right' },
  { header: 'Latency p50', render: (r) => latency(r.latencyP50Ms), align: 'right' },
  { header: 'Cases', render: (r) => `${r.approvedCaseCount}/${r.caseCount}`, align: 'right' },
  { header: 'Verdict', render: (r) => <VerdictTag approved={r.approved} /> },
]

export function RunsPage() {
  const state = useAsync(() => api.goldenRuns(), [])

  return (
    <>
      <PageHeader
        title="Golden runs"
        sub="One row per model run against the golden set. A run is approved only when all five gates pass; cases counts how many cases had at least four clean rounds in five."
      />
      <Async state={state}>
        {(rows) =>
          rows.length === 0 ? (
            <EmptyState title="No golden runs stored yet" />
          ) : (
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(r) => r.id}
              rowHref={(r) => `/runs/${r.id}`}
            />
          )
        }
      </Async>
    </>
  )
}
