import { useParams } from 'react-router-dom'
import { api } from '@/services/api'
import { Async } from '@/components/ui/Async'
import { DiffView } from '@/components/features/DiffView'
import { EvaluationsTable } from '@/components/features/EvaluationsTable'
import { FindingCard } from '@/components/features/FindingCard'
import { EmptyState } from '@/components/ui/States'
import { MetricStrip } from '@/components/ui/MetricStrip'
import { PageHeader } from '@/components/ui/PageHeader'
import { Eyebrow, Field, IdTag, Mono, Prose, Stack } from '@/components/ui/primitives'
import { useAsync } from '@/hooks/useAsync'
import { cost, dateTime, latency, tokens } from '@/utils/format'

export function AnalysisDetailPage() {
  const { id } = useParams()
  const analysisId = Number(id)
  // Chain in the reviewed diff so the findings can be read against it.
  const analysis = useAsync(async () => {
    const analysis = await api.analysis(analysisId)
    const diff = await api.diff(analysis.diffId)
    return { analysis, diff }
  }, [analysisId])
  const evaluations = useAsync(() => api.analysisEvaluations(analysisId), [analysisId])

  return (
    <>
      <Async state={analysis}>
        {({ analysis: a, diff }) => (
          <>
            <PageHeader
              crumbs={[
                { label: 'Analyses', to: '/analyses' },
                { label: `Diff #${a.diffId}`, to: `/diffs/${a.diffId}` },
              ]}
              title={<IdTag>Analysis #{a.id}</IdTag>}
            />
            <Stack $gap={32}>
              <MetricStrip
                metrics={[
                  { key: 'Engine', value: a.engine ?? '—' },
                  { key: 'Model', value: a.model ?? '—' },
                  { key: 'Prompt', value: a.promptVersion ?? '—' },
                  { key: 'Cost', value: cost(a.cost) },
                  { key: 'Latency', value: latency(a.latencyMs) },
                  { key: 'Tokens', value: `${tokens(a.inputTokens)} / ${tokens(a.outputTokens)}` },
                  { key: 'Run', value: dateTime(a.createdAt) },
                ]}
              />

              {a.summary && (
                <Field>
                  <Eyebrow>Summary</Eyebrow>
                  <Prose>{a.summary}</Prose>
                </Field>
              )}

              <Field>
                <Eyebrow>Reviewed diff · #{diff.id}</Eyebrow>
                <DiffView content={diff.content} />
              </Field>

              <Field>
                <Eyebrow>Findings · {a.findings?.length ?? 0}</Eyebrow>
                {a.findings && a.findings.length > 0 ? (
                  <Stack $gap={12}>
                    {a.findings.map((f, i) => (
                      <FindingCard key={i} finding={f} />
                    ))}
                  </Stack>
                ) : (
                  <EmptyState title="No findings" hint="This review reported a clean diff." />
                )}
              </Field>

              <Field>
                <Eyebrow>Judge evaluations</Eyebrow>
                <Async state={evaluations}>
                  {(rows) =>
                    rows.length === 0 ? (
                      <EmptyState
                        title="Not judged yet"
                        hint={<>Score it with <Mono>dotnet run -- judge {analysisId}</Mono>.</>}
                      />
                    ) : (
                      <EvaluationsTable rows={rows} showAnalysis={false} />
                    )
                  }
                </Async>
              </Field>
            </Stack>
          </>
        )}
      </Async>
    </>
  )
}
