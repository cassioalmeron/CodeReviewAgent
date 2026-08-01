import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import styled from 'styled-components'
import { DiamondIcon } from '@/components/icons'
import { useProject } from '@/contexts/ProjectContext'

const Shell = styled.div`
  display: grid;
  grid-template-columns: 232px 1fr;
  min-height: 100%;

  @media (max-width: 820px) {
    grid-template-columns: 1fr;
  }
`

const Sidebar = styled.aside`
  border-right: 1px solid var(--border);
  padding: 22px 16px;
  display: flex;
  flex-direction: column;
  gap: 24px;
  position: sticky;
  top: 0;
  align-self: start;
  height: 100vh;

  @media (max-width: 820px) {
    position: static;
    height: auto;
    border-right: none;
    border-bottom: 1px solid var(--border);
  }
`

const Brand = styled.div`
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 8px;
`

const Mark = styled.div`
  width: 30px;
  height: 30px;
  border-radius: 7px;
  display: grid;
  place-items: center;
  background: var(--gold-soft);
  border: 1px solid color-mix(in srgb, var(--gold) 40%, transparent);
  color: var(--gold);
  font-family: var(--mono);
  font-weight: 700;
  font-size: 15px;
`

const BrandText = styled.div`
  display: flex;
  flex-direction: column;
  line-height: 1.2;
`

const BrandName = styled.span`
  font-weight: 600;
  font-size: 14px;
`

const BrandSub = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--faint);
`

const Group = styled.div`
  display: flex;
  flex-direction: column;
  gap: 8px;
`

const Label = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--faint);
  padding: 0 10px;
`

const Select = styled.select`
  appearance: none;
  width: 100%;
  padding: 9px 10px;
  border-radius: var(--radius-sm);
  background: var(--panel);
  border: 1px solid var(--border);
  color: var(--text);
  font-family: var(--sans);
  font-size: 13px;
  cursor: pointer;

  &:hover {
    border-color: color-mix(in srgb, var(--gold) 40%, var(--border));
  }
`

const AllLink = styled.button`
  align-self: flex-start;
  background: none;
  border: none;
  padding: 0 10px;
  color: var(--muted);
  font-family: var(--mono);
  font-size: 11px;
  cursor: pointer;

  &:hover {
    color: var(--gold);
  }
`

const Nav = styled.nav`
  display: flex;
  flex-direction: column;
  gap: 2px;
`

const Item = styled(NavLink)`
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 10px;
  border-radius: var(--radius-sm);
  color: var(--muted);
  font-size: 14px;
  transition: background 0.12s ease, color 0.12s ease;

  &:hover {
    background: var(--panel);
    color: var(--text);
  }

  &.active {
    background: var(--panel);
    color: var(--text);
  }

  &.active::before {
    content: '';
    width: 3px;
    height: 15px;
    border-radius: 2px;
    background: var(--gold);
    margin-left: -10px;
  }
`

const Main = styled.main`
  padding: 40px clamp(20px, 5vw, 56px) 80px;
  max-width: 1100px;
  width: 100%;
`

export function Layout() {
  const { projects, project, projectId, select, clear } = useProject()
  const navigate = useNavigate()

  // Judge evaluations only matter for the Golden Set (same criterion as the backend: Folder "golden").
  const isGolden = project?.folder === 'golden'

  return (
    <Shell>
      <Sidebar>
        <Brand>
          <Mark>
            <DiamondIcon size={16} />
          </Mark>
          <BrandText>
            <BrandName>Review Console</BrandName>
            <BrandSub>read-only</BrandSub>
          </BrandText>
        </Brand>

        {projectId != null && (
          <Group>
            <Label>Project</Label>
            <Select
              value={projectId}
              onChange={(e) => select(Number(e.target.value))}
              aria-label="Selected project"
            >
              {projects?.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </Select>
            <AllLink
              type="button"
              onClick={() => {
                clear()
                navigate('/')
              }}
            >
              ← All projects
            </AllLink>
          </Group>
        )}

        {projectId != null && (
          <Nav>
            <Label>Browse</Label>
            <Item to="/" end>
              Overview
            </Item>
            <Item to="/reviews">Reviews</Item>
            <Item to="/assessments">Assessments</Item>
            {isGolden && <Item to="/evaluations">Evaluations</Item>}
          </Nav>
        )}
      </Sidebar>
      <Main>
        <Outlet />
      </Main>
    </Shell>
  )
}
