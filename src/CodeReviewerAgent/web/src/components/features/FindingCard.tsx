import styled from 'styled-components'
import type { Finding } from '@/types'
import { CategoryTag, SeverityTag } from '@/components/ui/Tags'

const Card = styled.article`
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--panel);
  overflow: hidden;
`

const Head = styled.header`
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-soft);
  background: var(--panel-2);
`

const Location = styled.span`
  font-family: var(--mono);
  font-size: 12.5px;
  color: var(--text);
  margin-right: auto;
  word-break: break-all;
`

const LineNo = styled.span`
  color: var(--gold);
`

const Snippet = styled.pre`
  margin: 0;
  padding: 12px 16px;
  font-family: var(--mono);
  font-size: 12.5px;
  color: var(--text);
  background: var(--panel-2);
  border-bottom: 1px solid var(--border-soft);
  overflow-x: auto;
  white-space: pre;
`

const Body = styled.div`
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
`

const Block = styled.div`
  display: flex;
  flex-direction: column;
  gap: 4px;
`

const Label = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--faint);
`

const Text = styled.p`
  margin: 0;
  line-height: 1.6;
`

export function FindingCard({ finding }: { finding: Finding }) {
  return (
    <Card>
      <Head>
        <Location>
          {finding.file ?? 'unknown file'}
          {finding.line != null && (
            <>
              :<LineNo>{finding.line}</LineNo>
            </>
          )}
        </Location>
        <SeverityTag value={finding.severity} />
        <CategoryTag value={finding.category} />
      </Head>
      {finding.code_snippet && <Snippet>{finding.code_snippet}</Snippet>}
      <Body>
        {finding.problem && (
          <Block>
            <Label>Problem</Label>
            <Text>{finding.problem}</Text>
          </Block>
        )}
        {finding.suggestion && (
          <Block>
            <Label>Suggestion</Label>
            <Text>{finding.suggestion}</Text>
          </Block>
        )}
      </Body>
    </Card>
  )
}
