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

export function AssessmentDetailPage() {
  const { id } = useParams()
  const assessmentId = Number(id)
  // Chain in the reviewed diff so the findings can be read against it.
  const assessment = useAsync(async () => {
    const assessment = await api.assessment(assessmentId)
    const review = await api.review(assessment.reviewId)
    return { assessment, review }
  }, [assessmentId])
  const evaluations = useAsync(() => api.assessmentEvaluations(assessmentId), [assessmentId])

  return (
    <>
      <Async state={assessment}>
        {({ assessment: a, review }) => (
          <>
            <PageHeader
              crumbs={[
                { label: 'Assessments', to: '/assessments' },
                { label: `Review #${a.reviewId}`, to: `/reviews/${a.reviewId}` },
              ]}
              title={<IdTag>Assessment #{a.id}</IdTag>}
            />
            <Stack $gap={32}>
              <MetricStrip
                metrics={[
                  { key: 'Engine', value: a.engine ?? '—' },
                  { key: 'Model', value: a.model ?? '—' },
                  { key: 'Prompt', value: a.promptVersion ?? '—' },
                  { key: 'Skills', value: a.skills ?? '—' },
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
                <Eyebrow>Reviewed diff · #{review.id}</Eyebrow>
                <DiffView content={review.content} />
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
                        hint={<>Score it with <Mono>dotnet run -- judge {assessmentId}</Mono>.</>}
                      />
                    ) : (
                      <EvaluationsTable rows={rows} showAssessment={false} />
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
