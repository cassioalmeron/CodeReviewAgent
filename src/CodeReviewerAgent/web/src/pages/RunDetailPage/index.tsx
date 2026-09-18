import { useParams } from 'react-router-dom'
import styled from 'styled-components'
import { api } from '@/services/api'
import { Async } from '@/components/ui/Async'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { MetricStrip } from '@/components/ui/MetricStrip'
import { PageHeader } from '@/components/ui/PageHeader'
import { Eyebrow, Mono, Muted, Stack } from '@/components/ui/primitives'
import { VerdictTag } from '@/components/ui/VerdictTag'
import { useAsync } from '@/hooks/useAsync'
import type { GoldenCaseScore, GoldenGateView } from '@/types'
import { cost, dateTime, duration, latency, tokens } from '@/utils/format'

const Result = styled.span<{ $passed: boolean }>`
  font-family: var(--mono);
  font-size: 12px;
  color: ${(p) => (p.$passed ? 'var(--add)' : 'var(--critical)')};
`

const percent = (part: number, whole: number) =>
  whole === 0 ? '—' : `${((part / whole) * 100).toFixed(0)}%`

const gateColumns: Column<GoldenGateView>[] = [
  { header: 'Gate', render: (g) => g.name },
  { header: 'Measured', render: (g) => <Mono>{g.value}</Mono> },
  { header: 'Floor', render: (g) => <Mono>{g.floorText}</Mono> },
  {
    header: 'Result',
    render: (g) => <Result $passed={g.passed}>{g.passed ? 'pass' : 'fail'}</Result>,
  },
]

const caseColumns: Column<GoldenCaseScore>[] = [
  { header: 'Case', render: (c) => c.caseName },
  { header: 'Kind', render: (c) => <Muted>{c.kind}</Muted> },
  {
    header: 'Caught / resisted',
    render: (c) => `${c.successes}/${c.runs}`,
    align: 'right',
  },
  { header: 'Clean rounds', render: (c) => `${c.cleanRounds}/${c.runs}`, align: 'right' },
  {
    header: 'Precision',
    render: (c) => (
      <>
        {c.precisionCorrect}/{c.precisionCounted} <Muted>{percent(c.precisionCorrect, c.precisionCounted)}</Muted>
      </>
    ),
    align: 'right',
  },
  { header: 'Exact severity', render: (c) => c.exactCalibrations, align: 'right' },
  { header: 'Discarded', render: (c) => c.discardedFindings, align: 'right' },
  {
    header: 'Case',
    render: (c) => (
      <Result $passed={c.approved}>{c.approved ? 'approved' : 'not approved'}</Result>
    ),
  },
]

export function RunDetailPage() {
  const { id } = useParams()
  const runId = Number(id)
  const state = useAsync(() => api.goldenRun(runId), [runId])

  return (
    <Async state={state}>
      {({ run, gates, cases }) => (
        <>
          <PageHeader
            crumbs={[{ label: 'Golden runs', to: '/runs' }]}
            title={run.model}
            aside={<VerdictTag approved={run.approved} />}
            sub={`${run.engine ?? 'unknown engine'} · skills ${run.skills ?? '—'} · prompt ${run.promptVersion ?? '—'} · ${dateTime(run.startedAt)}`}
          />
          <Stack $gap={28}>
            <MetricStrip
              metrics={[
                { key: 'Reviews', value: String(run.reviewCount) },
                { key: 'Cost', value: cost(run.cost) },
                { key: 'Duration', value: duration(run.durationMs) },
                { key: 'Tokens', value: tokens(run.inputTokens + run.outputTokens) },
                { key: 'Latency p50', value: latency(run.latencyP50Ms) , hint: 'half the reviews answered within this' },
                { key: 'Latency p95', value: latency(run.latencyP95Ms) , hint: '95 of every 100 answered within this' },
                { key: 'Latency p99', value: latency(run.latencyP99Ms) , hint: '99 of every 100; the slow tail' },
              ]}
            />

            <Stack $gap={10}>
              <Eyebrow>Gates (ADR-015)</Eyebrow>
              <DataTable columns={gateColumns} rows={gates} rowKey={(g) => g.name} />
            </Stack>

            <Stack $gap={10}>
              <Eyebrow>
                Cases — {run.approvedCaseCount} of {run.caseCount} approved
              </Eyebrow>
              <DataTable columns={caseColumns} rows={cases} rowKey={(c) => c.id} />
            </Stack>

            {run.reportFile && (
              <Muted>
                <Mono>{run.reportFile}</Mono>
              </Muted>
            )}
          </Stack>
        </>
      )}
    </Async>
  )
}
