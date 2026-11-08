import { RealtimeStatus } from '@/features/realtime/RealtimeStatus'
import { PasswordDialog } from '@/features/auth/PasswordDialog'
import { useSession } from '@/features/auth/context'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
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
  LogOut,
  KeyRound,
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
  const [passwordOpen, setPasswordOpen] = useState(false)
  const { user, logout } = useSession()
  const [loggingOut, setLoggingOut] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const mobileTrigger = useRef<HTMLButtonElement>(null)
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
      {[
        ...navigation,
        ...(user?.role === 'Admin'
          ? [{ to: '/users', label: 'Users', icon: UserRound }]
          : []),
      ].map(({ to, label, icon: Icon }) => (
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
  useEffect(() => {
    document.title = `FleetFlow · ${location.pathname === '/' ? 'Operations overview' : location.pathname.startsWith('/shipments/new') ? 'Create shipment' : location.pathname.startsWith('/users') ? 'Users' : (navigation.find((item) => item.to !== '/' && location.pathname.startsWith(item.to))?.label ?? 'Workspace')}`
    // Navigation should place keyboard/screen-reader users at the new page, without
    // stealing focus when only a filter query parameter changes.
    if (document.activeElement?.closest('.sidebar'))
      document.getElementById('main-content')?.focus({ preventScroll: true })
  }, [location.pathname])
  const current = location.pathname.startsWith('/users')
    ? 'Users'
    : (navigation.find(
        (item) => item.to !== '/' && location.pathname.startsWith(item.to),
      )?.label ?? 'Overview')
  return (
    <div className={`app-layout ${collapsed ? 'sidebar-collapsed' : ''}`}>
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">
        <Link
          to="/"
          className="brand"
          aria-label={collapsed ? 'FleetFlow home' : undefined}
        >
          <span className="brand-mark">
            <Route size={24} />
          </span>
          {!collapsed && (
            <span>
              FleetFlow{' '}
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
              ref={mobileTrigger}
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
            <RealtimeStatus />
            <Button
              variant="ghost"
              size="sm"
              className="icon-button"
              aria-label="Sign out"
              title="Sign out"
              disabled={loggingOut}
              onClick={async () => {
                setLoggingOut(true)
                try {
                  await logout()
                } catch (error) {
                  toast.error(
                    error instanceof Error
                      ? error.message
                      : 'Sign out failed. Try again.',
                  )
                } finally {
                  setLoggingOut(false)
                }
              }}
            >
              <LogOut size={18} />
            </Button>
            <button
              className="icon-button"
              aria-label="Change password"
              title="Change password"
              onClick={() => setPasswordOpen(true)}
            >
              <KeyRound size={18} />
            </button>
            <div className="demo-user">
              <span className="user-avatar">
                <UserRound size={17} />
              </span>
              <div>
                <strong>{user?.displayName}</strong>
                <span>{user?.role}</span>
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
      {passwordOpen && <PasswordDialog close={() => setPasswordOpen(false)} />}
      <Dialog
        open={mobileOpen}
        returnFocusTo={mobileTrigger}
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
