import styled from 'styled-components'
import type { Category, Severity } from '@/types'

const severityColor: Record<Severity, string> = {
  Info: 'var(--info)',
  Warning: 'var(--warning)',
  Critical: 'var(--critical)',
}

/** Severity carries the color weight: a filled chip. */
const SeverityChip = styled.span<{ $color: string }>`
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-family: var(--mono);
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  padding: 3px 9px 3px 7px;
  border-radius: 999px;
  color: ${(p) => p.$color};
  background: color-mix(in srgb, ${(p) => p.$color} 14%, transparent);
  border: 1px solid color-mix(in srgb, ${(p) => p.$color} 34%, transparent);
  white-space: nowrap;

  &::before {
    content: '';
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: ${(p) => p.$color};
  }
`

export function SeverityTag({ value }: { value: Severity | null }) {
  if (!value) return <Muted>—</Muted>
  return <SeverityChip $color={severityColor[value]}>{value}</SeverityChip>
}

/** Category stays quiet: an outlined chip, no fill, so it recedes behind severity. */
const CategoryChip = styled.span`
  display: inline-flex;
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: 0.03em;
  padding: 3px 9px;
  border-radius: 999px;
  color: var(--muted);
  border: 1px solid var(--border);
  white-space: nowrap;
`

export function CategoryTag({ value }: { value: Category | null }) {
  if (!value) return <Muted>—</Muted>
  return <CategoryChip>{value}</CategoryChip>
}

const Muted = styled.span`
  color: var(--faint);
`
