import { NavLink, Outlet } from 'react-router-dom';
import { Activity, AppWindow, KeyRound, ScrollText, Users } from 'lucide-react';

export function Shell() {
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <KeyRound size={22} />
          <span>NetAuth0</span>
        </div>
        <nav>
          <NavLink to="/dashboard"><Activity size={18} /> Overview</NavLink>
          <NavLink to="/applications"><AppWindow size={18} /> Applications</NavLink>
          <NavLink to="/users"><Users size={18} /> Users</NavLink>
          <NavLink to="/audit"><ScrollText size={18} /> Audit</NavLink>
        </nav>
      </aside>
      <main className="main">
        <Outlet />
      </main>
    </div>
  );
}
