import { Link, Outlet, useLocation } from 'react-router-dom';
import { cn } from '@/lib/utils';

const navItems = [
  { path: '/', label: 'Dashboard' },
  { path: '/actions', label: 'Actions' },
  { path: '/entities', label: 'Entities' },
  { path: '/status', label: 'Status Effects' },
  { path: '/gambits', label: 'Gambits' },
  { path: '/combat', label: 'Combat Simulator' },
];

export function AppLayout() {
  const location = useLocation();

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b">
        <div className="container mx-auto px-4">
          <div className="flex items-center justify-between h-16">
            <h1 className="text-2xl font-bold">HeroScript Dashboard</h1>
            <nav>
              <ul className="flex gap-6">
                {navItems.map((item) => (
                  <li key={item.path}>
                    <Link
                      to={item.path}
                      className={cn(
                        'text-sm font-medium transition-colors hover:text-primary',
                        location.pathname === item.path
                          ? 'text-foreground'
                          : 'text-muted-foreground'
                      )}
                    >
                      {item.label}
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
          </div>
        </div>
      </header>
      <main className="container mx-auto px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
