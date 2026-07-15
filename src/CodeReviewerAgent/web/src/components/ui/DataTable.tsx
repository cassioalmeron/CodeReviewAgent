import type { ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import styled from 'styled-components'

export interface Column<T> {
  header: string
  render: (row: T) => ReactNode
  align?: 'left' | 'right'
  width?: string
}

const Wrap = styled.div`
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
  background: var(--panel);
`

const Scroll = styled.div`
  overflow-x: auto;
`

const Table = styled.table`
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
`

const Th = styled.th<{ $align?: string; $width?: string }>`
  text-align: ${(p) => p.$align ?? 'left'};
  width: ${(p) => p.$width ?? 'auto'};
  padding: 11px 16px;
  font-family: var(--mono);
  font-size: 10.5px;
  font-weight: 500;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--faint);
  background: var(--panel-2);
  border-bottom: 1px solid var(--border);
  white-space: nowrap;
`

const Tr = styled.tr<{ $clickable?: boolean }>`
  border-bottom: 1px solid var(--border-soft);
  transition: background 0.12s ease;
  cursor: ${(p) => (p.$clickable ? 'pointer' : 'default')};

  &:last-child {
    border-bottom: none;
  }
  &:hover {
    background: ${(p) => (p.$clickable ? 'var(--panel-2)' : 'transparent')};
  }
`

const Td = styled.td<{ $align?: string }>`
  text-align: ${(p) => p.$align ?? 'left'};
  padding: 12px 16px;
  color: var(--text);
  vertical-align: top;
`

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  rowHref,
}: {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string | number
  rowHref?: (row: T) => string
}) {
  const navigate = useNavigate()
  return (
    <Wrap>
      <Scroll>
        <Table>
          <thead>
            <tr>
              {columns.map((c, i) => (
                <Th key={i} $align={c.align} $width={c.width}>
                  {c.header}
                </Th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const href = rowHref?.(row)
              return (
                <Tr
                  key={rowKey(row)}
                  $clickable={!!href}
                  onClick={href ? () => navigate(href) : undefined}
                >
                  {columns.map((c, i) => (
                    <Td key={i} $align={c.align}>
                      {c.render(row)}
                    </Td>
                  ))}
                </Tr>
              )
            })}
          </tbody>
        </Table>
      </Scroll>
    </Wrap>
  )
}
