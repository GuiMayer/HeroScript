import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { QueryProvider } from '@/lib/query-provider';
import { AppLayout } from '@/components/layout/AppLayout';
import { DashboardPage } from '@/components/tools/DashboardPage';
import { ActionsPage } from '@/components/tools/ActionsPage';
import { EntitiesPage } from '@/components/tools/EntitiesPage';
import { StatusEffectsPage } from '@/components/tools/StatusEffectsPage';
import { GambitsPage } from '@/components/tools/GambitsPage';
import { CombatSimulatorPage } from '@/components/tools/CombatSimulatorPage';

function App() {
  return (
    <QueryProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<AppLayout />}>
            <Route index element={<DashboardPage />} />
            <Route path="actions" element={<ActionsPage />} />
            <Route path="entities" element={<EntitiesPage />} />
            <Route path="status" element={<StatusEffectsPage />} />
            <Route path="gambits" element={<GambitsPage />} />
            <Route path="combat" element={<CombatSimulatorPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryProvider>
  );
}

export default App;
