import { api } from '@/services/api'
import { Async } from '@/components/ui/Async'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { IdTag, Mono, Muted } from '@/components/ui/primitives'
import { useAsync } from '@/hooks/useAsync'
import { relativeDay, shortHash } from '@/utils/format'
import type { DiffListItem } from '@/types'

const columns: Column<DiffListItem>[] = [
  { header: 'ID', width: '64px', render: (d) => <IdTag>#{d.id}</IdTag> },
  { header: 'Source', render: (d) => <Mono>{d.source ?? '—'}</Mono> },
  { header: 'Content hash', render: (d) => <Muted>{shortHash(d.contentHash)}</Muted> },
  {
    header: 'Analyses',
    align: 'right',
    width: '90px',
    render: (d) => <Mono>{d.analysisCount}</Mono>,
  },
  {
    header: 'Captured',
    align: 'right',
    width: '110px',
    render: (d) => <Muted>{relativeDay(d.createdAt)}</Muted>,
  },
]

export function DiffsPage() {
  const state = useAsync(() => api.diffs(), [])
  return (
    <>
      <PageHeader
        title="Diffs"
        sub="Every captured diff the agent has stored. Open one to see its analyses."
      />
      <Async state={state}>
        {(diffs) =>
          diffs.length === 0 ? (
            <EmptyState
              title="No diffs stored yet"
              hint="Run the CLI (e.g. dotnet run -- eval) to capture diffs into the store."
            />
          ) : (
            <DataTable
              columns={columns}
              rows={diffs}
              rowKey={(d) => d.id}
              rowHref={(d) => `/diffs/${d.id}`}
            />
          )
        }
      </Async>
    </>
  )
}
