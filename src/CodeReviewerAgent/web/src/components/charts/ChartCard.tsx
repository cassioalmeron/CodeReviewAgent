import type { ReactNode } from 'react'
import styled from 'styled-components'

const Card = styled.section<{ $wide?: boolean }>`
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 18px 18px 14px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  grid-column: ${(p) => (p.$wide ? '1 / -1' : 'auto')};
`

const Head = styled.div`
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
`

const Title = styled.h3`
  margin: 0;
  font-size: 14px;
  font-weight: 600;
`

const Hint = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--faint);
`

const Empty = styled.div`
  display: grid;
  place-items: center;
  min-height: 160px;
  color: var(--faint);
  font-size: 13px;
`

/** Uniform frame for a dashboard chart: title, optional hint, and an empty fallback. */
export function ChartCard({
  title,
  hint,
  wide,
  empty,
  children,
}: {
  title: string
  hint?: ReactNode
  wide?: boolean
  empty?: boolean
  children: ReactNode
}) {
  return (
    <Card $wide={wide}>
      <Head>
        <Title>{title}</Title>
        {hint && <Hint>{hint}</Hint>}
      </Head>
      {empty ? <Empty>No data yet</Empty> : children}
    </Card>
  )
}
