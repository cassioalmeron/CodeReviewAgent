import type { ReactNode } from 'react'
import styled from 'styled-components'

const Box = styled.div`
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 64px 24px;
  text-align: center;
  border: 1px dashed var(--border);
  border-radius: var(--radius);
  color: var(--muted);
`

const Title = styled.p`
  margin: 0;
  color: var(--text);
  font-weight: 500;
`

const Sub = styled.p`
  margin: 0;
  font-size: 13px;
  color: var(--muted);
  max-width: 46ch;
`

const Bar = styled.div`
  height: 3px;
  border-radius: 2px;
  background: linear-gradient(90deg, transparent, var(--gold), transparent);
  background-size: 40% 100%;
  background-repeat: no-repeat;
  animation: slide 1.1s ease-in-out infinite;

  @keyframes slide {
    0% {
      background-position: -40% 0;
    }
    100% {
      background-position: 140% 0;
    }
  }
`

export function Loading() {
  return (
    <div style={{ padding: '2px 0' }}>
      <Bar aria-label="Loading" />
    </div>
  )
}

export function ErrorState({ message }: { message: string }) {
  return (
    <Box>
      <Title>Couldn’t load this</Title>
      <Sub>{message}. Check that the viewer API is running on port 5180.</Sub>
    </Box>
  )
}

export function EmptyState({ title, hint }: { title: string; hint?: ReactNode }) {
  return (
    <Box>
      <Title>{title}</Title>
      {hint && <Sub>{hint}</Sub>}
    </Box>
  )
}
