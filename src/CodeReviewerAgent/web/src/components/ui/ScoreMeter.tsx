import styled from 'styled-components'

const Row = styled.div<{ $emphasis?: boolean }>`
  display: grid;
  grid-template-columns: 1fr auto auto;
  align-items: center;
  gap: 14px;
  padding: 9px 0;

  & + & {
    border-top: 1px solid var(--border-soft);
  }

  ${(p) =>
    p.$emphasis &&
    `
    & > label { color: var(--text); font-weight: 600; }
  `}
`

const Label = styled.label`
  font-size: 13px;
  color: var(--muted);
`

const Segments = styled.div`
  display: flex;
  gap: 4px;
`

const Segment = styled.span<{ $on: boolean }>`
  width: 26px;
  height: 7px;
  border-radius: 2px;
  background: ${(p) => (p.$on ? 'var(--gold)' : 'var(--border)')};
  box-shadow: ${(p) => (p.$on ? '0 0 8px var(--gold-soft)' : 'none')};
`

const Value = styled.span`
  font-family: var(--mono);
  font-size: 13px;
  color: var(--text);
  min-width: 34px;
  text-align: right;
`

/** A 1–5 rubric score as a segmented readout — the console's signature element. */
export function ScoreMeter({
  label,
  value,
  emphasis,
}: {
  label: string
  value: number
  emphasis?: boolean
}) {
  return (
    <Row $emphasis={emphasis}>
      <Label>{label}</Label>
      <Segments aria-hidden>
        {[1, 2, 3, 4, 5].map((n) => (
          <Segment key={n} $on={n <= value} />
        ))}
      </Segments>
      <Value>{value}/5</Value>
    </Row>
  )
}
