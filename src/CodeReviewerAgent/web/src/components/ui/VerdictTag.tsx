import styled from 'styled-components'

const Chip = styled.span<{ $color: string }>`
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

/**
 * Approved or not, the verdict of ADR-015: every gate has to pass, and none is averaged with
 * another, so there is no middle chip here on purpose.
 */
export function VerdictTag({ approved }: { approved: boolean }) {
  return (
    <Chip $color={approved ? 'var(--add)' : 'var(--critical)'}>
      {approved ? 'Approved' : 'Not approved'}
    </Chip>
  )
}
