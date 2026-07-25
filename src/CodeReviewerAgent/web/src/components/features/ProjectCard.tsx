import styled from 'styled-components'
import type { ProjectListItem } from '@/types'
import { relativeDay } from '@/utils/format'

const Card = styled.button`
  text-align: left;
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 18px;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 14px;
  transition: border-color 0.14s ease, transform 0.14s ease;
  font: inherit;
  color: inherit;

  &:hover {
    border-color: color-mix(in srgb, var(--gold) 45%, var(--border));
    transform: translateY(-2px);
  }
`

const Top = styled.div`
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
`

const Name = styled.h3`
  margin: 0;
  font-size: 16px;
  font-weight: 600;
`

const Folder = styled.div`
  font-family: var(--mono);
  font-size: 11px;
  color: var(--faint);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 22ch;
`

const Badge = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--gold);
  background: var(--gold-soft);
  border: 1px solid color-mix(in srgb, var(--gold) 34%, transparent);
  padding: 3px 8px;
  border-radius: 999px;
  white-space: nowrap;
`

const Stats = styled.div`
  display: flex;
  gap: 20px;
`

const Stat = styled.div`
  display: flex;
  flex-direction: column;
`

const N = styled.span`
  font-family: var(--mono);
  font-size: 20px;
  color: var(--text);
`

const L = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--faint);
`

const Foot = styled.div`
  font-family: var(--mono);
  font-size: 11px;
  color: var(--muted);
  border-top: 1px solid var(--border-soft);
  padding-top: 12px;
`

export function ProjectCard({
  project,
  onSelect,
}: {
  project: ProjectListItem
  onSelect: (id: number) => void
}) {
  return (
    <Card type="button" onClick={() => onSelect(project.id)}>
      <Top>
        <div>
          <Name>{project.name}</Name>
          <Folder title={project.folder}>{project.folder}</Folder>
        </div>
        {project.folder === 'golden' && <Badge>golden</Badge>}
      </Top>
      <Stats>
        <Stat>
          <N>{project.reviewCount}</N>
          <L>reviews</L>
        </Stat>
        <Stat>
          <N>{project.assessmentCount}</N>
          <L>assessments</L>
        </Stat>
      </Stats>
      <Foot>
        {project.lastAssessmentAt
          ? `last assessment · ${relativeDay(project.lastAssessmentAt)}`
          : 'no assessments yet'}
      </Foot>
    </Card>
  )
}
