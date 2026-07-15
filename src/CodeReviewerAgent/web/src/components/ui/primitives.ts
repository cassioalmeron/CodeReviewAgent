import styled from 'styled-components'

/** A surface panel — the base container for content blocks. */
export const Panel = styled.section`
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
`

/** Small uppercase label that names a region or field. */
export const Eyebrow = styled.span`
  font-family: var(--mono);
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--faint);
`

export const Mono = styled.span`
  font-family: var(--mono);
`

export const Muted = styled.span`
  color: var(--muted);
`

/** Page heading. */
export const PageTitle = styled.h1`
  margin: 0;
  font-size: 26px;
  font-weight: 600;
  letter-spacing: -0.01em;
`

export const SectionTitle = styled.h2`
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--muted);
`

/** Prose block for summaries / rationales. */
export const Prose = styled.p`
  margin: 0;
  color: var(--text);
  line-height: 1.65;
  max-width: 74ch;
`

/** Monospace record id, e.g. #12 — the gold accent marks identity. */
export const IdTag = styled.span`
  font-family: var(--mono);
  font-size: 13px;
  color: var(--gold);
`

/** Vertical rhythm helper. */
export const Stack = styled.div<{ $gap?: number }>`
  display: flex;
  flex-direction: column;
  gap: ${(p) => p.$gap ?? 24}px;
`

/** A labelled block: eyebrow above content. */
export const Field = styled.div`
  display: flex;
  flex-direction: column;
  gap: 10px;
`
