import styled from 'styled-components'

type LineKind = 'add' | 'remove' | 'meta' | 'hunk' | 'context'

function classify(line: string): LineKind {
  if (line.startsWith('+++') || line.startsWith('---') || line.startsWith('diff ') || line.startsWith('index '))
    return 'meta'
  if (line.startsWith('@@')) return 'hunk'
  if (line.startsWith('+')) return 'add'
  if (line.startsWith('-')) return 'remove'
  return 'context'
}

const Frame = styled.div`
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
  background: var(--panel-2);
`

const Scroll = styled.div`
  overflow-x: auto;
  font-family: var(--mono);
  font-size: 12.5px;
  line-height: 1.7;
`

const Line = styled.div<{ $kind: LineKind }>`
  display: grid;
  grid-template-columns: 44px 1fr;
  white-space: pre;
  background: ${(p) =>
    p.$kind === 'add' ? 'var(--add-bg)' : p.$kind === 'remove' ? 'var(--remove-bg)' : 'transparent'};
  color: ${(p) =>
    p.$kind === 'add'
      ? 'var(--add)'
      : p.$kind === 'remove'
        ? 'var(--remove)'
        : p.$kind === 'hunk'
          ? 'var(--info)'
          : p.$kind === 'meta'
            ? 'var(--faint)'
            : 'var(--text)'};
`

const Gutter = styled.span<{ $kind: LineKind }>`
  user-select: none;
  text-align: center;
  color: var(--faint);
  border-right: 1px solid var(--border);
  background: ${(p) =>
    p.$kind === 'add' ? 'var(--add-bg)' : p.$kind === 'remove' ? 'var(--remove-bg)' : 'var(--panel)'};
`

const Code = styled.span`
  padding: 0 14px;
`

export function DiffView({ content }: { content: string }) {
  const lines = content.replace(/\n$/, '').split('\n')
  return (
    <Frame>
      <Scroll>
        {lines.map((line, i) => {
          const kind = classify(line)
          const marker = kind === 'add' ? '+' : kind === 'remove' ? '−' : ''
          return (
            <Line key={i} $kind={kind}>
              <Gutter $kind={kind}>{marker}</Gutter>
              <Code>{line || ' '}</Code>
            </Line>
          )
        })}
      </Scroll>
    </Frame>
  )
}
