import { useEffect, useState } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { LayoutDashboard, ArrowLeftRight, PiggyBank, Target, ChartColumnBig, Repeat2, WalletCards, Settings, LogOut, Menu, X, Sparkles, SlidersHorizontal, Bell } from "lucide-react";
import { useAuthStore } from "../../store/auth-store";
import { api } from "../../services/api/client";

const navigation = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard },
  { to: "/transactions", label: "Transactions", icon: ArrowLeftRight },
  { to: "/budgets", label: "Budgets", icon: PiggyBank },
  { to: "/goals", label: "Goals", icon: Target },
  { to: "/reports", label: "Reports", icon: ChartColumnBig },
  { to: "/insights", label: "Insights", icon: Sparkles },
  { to: "/rules", label: "Rules", icon: SlidersHorizontal },
  { to: "/notifications", label: "Notifications", icon: Bell },
  { to: "/recurring", label: "Recurring", icon: Repeat2 },
  { to: "/accounts", label: "Accounts", icon: WalletCards },
  { to: "/settings", label: "Settings", icon: Settings },
];

export function AppShell({ title, children }: { title: string; children: React.ReactNode }) {
  const session = useAuthStore((state) => state.session);
  const clearSession = useAuthStore((state) => state.clearSession);
  const location = useLocation();
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const { data: unreadNotifications } = useQuery({
    queryKey: ["notifications-unread-count"],
    queryFn: async () => (await api.get<{ count: number }>("/notifications/unread-count")).data.count,
    enabled: Boolean(session),
    refetchInterval: 30000,
  });
  const displayName = session?.displayName ?? "User";
  const initials = displayName
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");

  useEffect(() => {
    setIsMenuOpen(false);
  }, [location.pathname]);

  return (
    <div className={`app-shell ${isMenuOpen ? "app-shell--menu-open" : ""}`.trim()}>
      <div className={`sidebar-backdrop ${isMenuOpen ? "sidebar-backdrop--visible" : ""}`.trim()} onClick={() => setIsMenuOpen(false)} aria-hidden={!isMenuOpen} />
      <aside className={`sidebar ${isMenuOpen ? "sidebar--open" : ""}`.trim()}>
        <div className="brand">
          <div className="brand__badge">TM</div>
          <div>
            <strong>TrackMint</strong>
            <span>Money clarity</span>
          </div>
        </div>

        <p className="sidebar__section-label">Workspace</p>
        <nav className="sidebar__nav">
          {navigation.map((item) => {
            const Icon = item.icon;
            return (
              <NavLink key={item.to} to={item.to} end={item.to === "/"} className={({ isActive }) => `nav-link ${isActive ? "nav-link--active" : ""}`}>
                <Icon size={18} />
                <span>{item.label}</span>
                {item.to === "/notifications" && (unreadNotifications ?? 0) > 0 && (
                  <span className="nav-link__badge">{unreadNotifications}</span>
                )}
              </NavLink>
            );
          })}
        </nav>

        <button type="button" className="ghost-button sidebar__logout" onClick={clearSession}>
          <LogOut size={16} />
          <span>Sign out</span>
        </button>
      </aside>

      <main className="main-panel">
        <header className="topbar">
          <div>
            <div className="topbar__title-row">
              <button type="button" className="ghost-button mobile-menu-button" onClick={() => setIsMenuOpen((current) => !current)} aria-label={isMenuOpen ? "Close navigation" : "Open navigation"}>
                {isMenuOpen ? <X size={18} /> : <Menu size={18} />}
              </button>
              <p className="eyebrow">Finance cockpit</p>
            </div>
            <h1>{title}</h1>
          </div>
          <div className="topbar__profile">
            <div className="topbar__profile-badge">{initials || "U"}</div>
            <div>
              <span>{displayName}</span>
              <small>{session?.email}</small>
            </div>
          </div>
        </header>

        <div className="page-content">{children}</div>
      </main>
    </div>
  );
}
