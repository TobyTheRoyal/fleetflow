import { PageBoundary } from '@/components/shared/PageBoundary'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, useLocation, useNavigate } from 'react-router-dom'
import {
  ArrowUpRight,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  LayoutDashboard,
  Menu,
  Package,
  Search,
  Truck,
  Users,
  UserRound,
  Route,
  PanelLeftClose,
} from 'lucide-react'
import { Dialog } from '@/components/ui/dialog'
import { SystemHealth } from './SystemHealth'
import { apiBase } from '@/lib/api'
const navigation = [
  { to: '/', label: 'Overview', icon: LayoutDashboard },
  { to: '/shipments', label: 'Shipments', icon: Package },
  { to: '/customers', label: 'Customers', icon: Users },
  { to: '/drivers', label: 'Drivers', icon: UserRound },
  { to: '/vehicles', label: 'Vehicles', icon: Truck },
]
export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const search = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()
  const location = useLocation()
  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key === 'k') {
        event.preventDefault()
        search.current?.focus()
      }
    }
    document.addEventListener('keydown', shortcut)
    return () => document.removeEventListener('keydown', shortcut)
  }, [])
  const links = (mobile = false) => (
    <nav aria-label={mobile ? 'Mobile navigation' : 'Main navigation'}>
      {navigation.map(({ to, label, icon: Icon }) => (
        <NavLink
          key={to}
          to={to}
          end={to === '/'}
          className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
          title={collapsed && !mobile ? label : undefined}
          onClick={() => setMobileOpen(false)}
        >
          <Icon size={19} />
          <span className={collapsed && !mobile ? 'sr-only' : ''}>{label}</span>
        </NavLink>
      ))}
    </nav>
  )
  const current =
    navigation.find(
      (item) => item.to !== '/' && location.pathname.startsWith(item.to),
    )?.label ?? 'Overview'
  return (
    <div className={`app-layout ${collapsed ? 'sidebar-collapsed' : ''}`}>
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">
        <Link to="/" className="brand" aria-label="FleetFlow home">
          <span className="brand-mark">
            <Route size={24} />
          </span>
          {!collapsed && (
            <span>
              FleetFlow
              <span className="brand-subtitle">LOGISTICS WORKSPACE</span>
            </span>
          )}
        </Link>
        <div className={`nav-caption ${collapsed ? 'sr-only' : ''}`}>
          WORKSPACE
        </div>
        {links()}
        <div className="sidebar-footer">
          {!collapsed && (
            <>
              <div className="workspace-note">
                <span className="workspace-icon">
                  <Truck size={18} />
                </span>
                <div>
                  <strong>Dispatch workspace</strong>
                  <span>Local development</span>
                </div>
              </div>
              <a
                className="docs-link"
                href={`${apiBase}/openapi/v1.json`}
                target="_blank"
                rel="noreferrer"
              >
                API documentation
                <ArrowUpRight size={14} />
                <span className="sr-only"> (opens in a new tab)</span>
              </a>
            </>
          )}
          <button
            className="collapse-button"
            onClick={() => setCollapsed(!collapsed)}
            aria-expanded={!collapsed}
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            {collapsed ? (
              <ChevronsRight size={17} />
            ) : (
              <>
                <ChevronsLeft size={17} />
                <span>Collapse sidebar</span>
              </>
            )}
          </button>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="flex items-center gap-3">
            <button
              className="icon-button mobile-toggle"
              aria-label="Open navigation"
              onClick={() => setMobileOpen(true)}
            >
              <Menu size={20} />
            </button>
            <div className="breadcrumbs">
              <span>Workspace</span>
              <ChevronRight size={13} />
              <strong>{current}</strong>
            </div>
          </div>
          <div className="topbar-actions">
            <form
              className="global-search"
              role="search"
              onSubmit={(event) => {
                event.preventDefault()
                const value = search.current?.value.trim()
                navigate(
                  `/shipments${value ? '?search=' + encodeURIComponent(value) : ''}`,
                )
                search.current?.blur()
              }}
            >
              <Search size={16} />
              <input
                ref={search}
                aria-label="Search all shipments"
                placeholder="Search shipments…"
                maxLength={100}
              />
              <kbd>⌘ K</kbd>
            </form>
            <SystemHealth />
            <div className="demo-user">
              <span className="user-avatar">
                <UserRound size={17} />
              </span>
              <div>
                <strong>Dispatcher</strong>
                <span>Local demo</span>
              </div>
            </div>
          </div>
        </header>
        <main id="main-content" tabIndex={-1} className="main-content">
          <PageBoundary />
        </main>
        <footer className="workspace-footer">
          <span>FleetFlow · Independent portfolio project</span>
          <span>All times in Europe/Vienna · Local workspace</span>
        </footer>
      </div>
      <Dialog
        open={mobileOpen}
        onOpenChange={setMobileOpen}
        title="FleetFlow workspace"
        description="Navigate your logistics operations."
      >
        <div className="mobile-navigation">{links(true)}</div>
        <button
          className="text-button mt-4"
          onClick={() => setMobileOpen(false)}
        >
          <PanelLeftClose size={16} />
          Close navigation
        </button>
      </Dialog>
    </div>
  )
}
