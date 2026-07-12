# Content Editor Dashboard - Implementation Plan

**Version:** 2.0  
**Date:** 2026-07-12  
**Status:** Ready for execution  
**Target Users:** Game designers and modders

---

## EXECUTIVE SUMMARY

This document contains the complete implementation plan for the **Content Editor Dashboard** for HeroScript. The dashboard is a React SPA that enables game designers and modders to create and edit game content (Actions, Entities, Status Effects, Gambits) through a visual interface, plus testing tools for formula evaluation and combat simulation.

### Objective

Build a web-based Content Editor Dashboard that allows non-programmers to:
- Create/Edit/Delete Actions (cards/abilities)
- Create/Edit/Delete Entities (heroes/enemies)
- Create/Edit/Delete Status Effects (buffs/debuffs)
- Create/Edit/Delete Gambits (AI behavior rules)
- Test formulas in real-time
- Simulate combats visually
- View event logs

### Current Status Analysis

**Backend API:** ✅ **COMPLETE** - All CRUD endpoints already implemented!
- ActionController: Full CRUD + reload + validate
- EntityController: Full CRUD + validate
- StatusEffectController: Full CRUD
- GambitController: Full CRUD + reload + decide
- MathExpressionController: Formula evaluation
- CombatController: Combat simulation
- EventsController: Event logs

**Frontend Dashboard:** ❌ **DOES NOT EXIST** - Needs complete implementation

### Architectural Alignment

This plan aligns with:
- `docs/dashboard/architecture.md` (588 lines of detailed architecture)
- `docs/dashboard/roadmap.md` (865 lines of implementation roadmap)

**Key differences from previous plan:**
- ❌ OLD: "Settings Manager" for config management (WRONG SCOPE)
- ✅ NEW: "Content Editor" for game design tooling (CORRECT SCOPE)

### Technology Stack

**Frontend:**
- React 18+ with TypeScript
- Vite (build tool, hot reload)
- TailwindCSS (utility-first CSS)
- Shadcn/ui (pre-built components)
- React Query (server state, caching)
- Zustand (client state)
- React Hook Form + Zod (forms & validation)
- React Router (navigation)
- Axios (HTTP client)
- Lucide React (icons)

**Backend:**
- ASP.NET Core API (already implemented)
- JSON file persistence (already implemented)

### Total Time Estimate

Based on `docs/dashboard/roadmap.md`:
- **Phase 1: Project Setup & API Client** - 1 day
- **Phase 2: Layout & Navigation** - 1-2 days
- **Phase 3: Content Editors** - 4-5 days
- **Phase 4: Testing Tools** - 2-3 days
- **Phase 5: Polish & Deploy** - 1-2 days
- **TOTAL:** 8-12 days

---

## PHASE 1: PROJECT SETUP & API CLIENT (1 day)

### 1.1 Initialize React Project (2-3 hours)

**Location:** `tools/Dashboard/`

**Steps:**

```bash
# Create project
cd tools
npm create vite@latest Dashboard -- --template react-ts
cd Dashboard
npm install

# Install dependencies
npm install axios react-router-dom zustand
npm install @tanstack/react-query
npm install react-hook-form zod @hookform/resolvers
npm install lucide-react

# Install TailwindCSS
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p

# Install Shadcn/ui
npx shadcn-ui@latest init
npx shadcn-ui@latest add button input label textarea select
npx shadcn-ui@latest add table dialog form card tabs
npx shadcn-ui@latest add toast skeleton alert-dialog
```

**Configure `tailwind.config.js`:**
```javascript
/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {},
  },
  plugins: [],
}
```

**Configure `vite.config.ts`:**
```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5260',
        changeOrigin: true,
      },
    },
  },
})
```

**Create folder structure:**
```
tools/Dashboard/src/
├── components/
│   ├── editors/              # Content CRUD editors
│   │   ├── ActionEditor.tsx
│   │   ├── EntityEditor.tsx
│   │   ├── StatusEditor.tsx
│   │   └── GambitEditor.tsx
│   ├── tools/                # Testing tools
│   │   ├── FormulaTester.tsx
│   │   ├── CombatSimulator.tsx
│   │   └── EventViewer.tsx
│   ├── ui/                   # Shadcn/ui components
│   └── layout/               # Layout components
│       ├── Layout.tsx
│       ├── Sidebar.tsx
│       ├── Header.tsx
│       └── Footer.tsx
├── services/                 # API clients
│   ├── api.ts
│   ├── actions.ts
│   ├── entities.ts
│   ├── status.ts
│   ├── gambits.ts
│   ├── combat.ts
│   ├── math.ts
│   └── events.ts
├── hooks/                    # React Query hooks
│   ├── useActions.ts
│   ├── useEntities.ts
│   ├── useStatus.ts
│   ├── useGambits.ts
│   ├── useCombat.ts
│   └── useEvents.ts
├── types/                    # TypeScript types
│   ├── action.ts
│   ├── entity.ts
│   ├── status.ts
│   ├── gambit.ts
│   ├── combat.ts
│   └── api.ts
├── stores/                   # Zustand stores
│   └── uiStore.ts
├── utils/                    # Utility functions
│   ├── validation.ts
│   ├── formatters.ts
│   └── constants.ts
├── App.tsx
├── main.tsx
└── routes.tsx
```

### 1.2 API Client Setup (2-3 hours)

**File:** `src/services/api.ts`

```typescript
import axios from 'axios';

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5260';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 10000,
});

// Request interceptor (add correlation ID)
apiClient.interceptors.request.use(
  (config) => {
    const correlationId = crypto.randomUUID();
    config.headers['X-Correlation-ID'] = correlationId;
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor (error handling)
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    console.error('API Error:', {
      url: error.config?.url,
      method: error.config?.method,
      status: error.response?.status,
      message: error.response?.data?.error || error.message,
    });
    return Promise.reject(error);
  }
);
```

**File:** `src/services/actions.ts`

```typescript
import { apiClient } from './api';
import { Action, CreateActionDto, UpdateActionDto } from '@/types/action';

export const actionsApi = {
  getAll: () => apiClient.get<Action[]>('/api/actions'),
  
  getById: (id: string) => 
    apiClient.get<Action>(`/api/actions/${id}`),
  
  getByType: (type: string) => 
    apiClient.get<Action[]>(`/api/actions/by-type/${type}`),
  
  getByTag: (tag: string) => 
    apiClient.get<Action[]>(`/api/actions/by-tag/${tag}`),
  
  create: (dto: CreateActionDto) => 
    apiClient.post<Action>('/api/actions', dto),
  
  update: (id: string, dto: UpdateActionDto) => 
    apiClient.put<Action>(`/api/actions/${id}`, dto),
  
  delete: (id: string) => 
    apiClient.delete(`/api/actions/${id}`),
  
  reload: (configName: string = 'default') => 
    apiClient.post(`/api/actions/reload?configName=${configName}`),
  
  validate: (dto: CreateActionDto) => 
    apiClient.post('/api/actions/validate', dto),
};
```

**Create similar files for:**
- `src/services/entities.ts` (EntityController endpoints)
- `src/services/status.ts` (StatusEffectController endpoints)
- `src/services/gambits.ts` (GambitController endpoints)
- `src/services/combat.ts` (CombatController endpoints)
- `src/services/math.ts` (MathExpressionController endpoints)
- `src/services/events.ts` (EventsController endpoints)

### 1.3 TypeScript Types (1-2 hours)

**File:** `src/types/action.ts`

```typescript
export enum ActionType {
  ATTACK = 'ATTACK',
  DEFENSE = 'DEFENSE',
  UTILITY = 'UTILITY',
  SPELL = 'SPELL',
}

export interface Action {
  actionId: string;
  displayName: string;
  description: string;
  actionType: ActionType;
  costs: ActionCosts;
  effects: EffectDefinition[];
  requiresTarget: boolean;
  multiTarget: boolean;
  cooldown: number;
  tags: string[];
}

export interface ActionCosts {
  resources: ResourceCost[];
}

export interface ResourceCost {
  resourceId: string;
  amount: number;
}

export interface EffectDefinition {
  effectId: string;
  type: string;
  target: string;
  value: number;
  timing: string;
}

export type CreateActionDto = Omit<Action, 'actionId'> & { actionId: string };
export type UpdateActionDto = Action;
```

**Create similar type files for:**
- `src/types/entity.ts`
- `src/types/status.ts`
- `src/types/gambit.ts`
- `src/types/combat.ts`
- `src/types/api.ts` (generic API response types)

### 1.4 React Query Hooks (1-2 hours)

**File:** `src/hooks/useActions.ts`

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { actionsApi } from '@/services/actions';
import { CreateActionDto, UpdateActionDto } from '@/types/action';
import { toast } from '@/components/ui/use-toast';

export function useActions() {
  return useQuery({
    queryKey: ['actions'],
    queryFn: async () => {
      const response = await actionsApi.getAll();
      return response.data;
    },
  });
}

export function useAction(id: string) {
  return useQuery({
    queryKey: ['actions', id],
    queryFn: async () => {
      const response = await actionsApi.getById(id);
      return response.data;
    },
    enabled: !!id,
  });
}

export function useCreateAction() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (dto: CreateActionDto) => {
      const response = await actionsApi.create(dto);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['actions'] });
      toast({
        title: 'Action created',
        description: 'Action created successfully',
      });
    },
    onError: (error: any) => {
      toast({
        title: 'Error',
        description: error.response?.data?.error || 'Failed to create action',
        variant: 'destructive',
      });
    },
  });
}

export function useUpdateAction() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: UpdateActionDto }) => {
      const response = await actionsApi.update(id, dto);
      return response.data;
    },
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['actions'] });
      queryClient.invalidateQueries({ queryKey: ['actions', variables.id] });
      toast({
        title: 'Action updated',
        description: 'Action updated successfully',
      });
    },
    onError: (error: any) => {
      toast({
        title: 'Error',
        description: error.response?.data?.error || 'Failed to update action',
        variant: 'destructive',
      });
    },
  });
}

export function useDeleteAction() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (id: string) => {
      await actionsApi.delete(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['actions'] });
      toast({
        title: 'Action deleted',
        description: 'Action deleted successfully',
      });
    },
    onError: (error: any) => {
      toast({
        title: 'Error',
        description: error.response?.data?.error || 'Failed to delete action',
        variant: 'destructive',
      });
    },
  });
}
```

**Create similar hook files for:**
- `src/hooks/useEntities.ts`
- `src/hooks/useStatus.ts`
- `src/hooks/useGambits.ts`
- `src/hooks/useCombat.ts`
- `src/hooks/useEvents.ts`

---

## PHASE 2: LAYOUT & NAVIGATION (1-2 days)

### 2.1 Basic Layout Structure (4-5 hours)

**File:** `src/components/layout/Layout.tsx`

```typescript
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { Header } from './Header';
import { Footer } from './Footer';

export function Layout() {
  return (
    <div className="flex h-screen bg-background">
      <Sidebar />
      <div className="flex flex-col flex-1 overflow-hidden">
        <Header />
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
        <Footer />
      </div>
    </div>
  );
}
```

**File:** `src/components/layout/Sidebar.tsx`

```typescript
import { Link, useLocation } from 'react-router-dom';
import {
  LayoutDashboard,
  Sword,
  Users,
  Zap,
  Brain,
  TestTube,
  Swords,
  FileText,
} from 'lucide-react';

const menuItems = [
  { icon: LayoutDashboard, label: 'Dashboard', path: '/' },
  {
    label: 'Content',
    items: [
      { icon: Sword, label: 'Actions', path: '/actions' },
      { icon: Users, label: 'Entities', path: '/entities' },
      { icon: Zap, label: 'Status Effects', path: '/status' },
      { icon: Brain, label: 'Gambits', path: '/gambits' },
    ],
  },
  {
    label: 'Tools',
    items: [
      { icon: TestTube, label: 'Formula Tester', path: '/tools/formula' },
      { icon: Swords, label: 'Combat Simulator', path: '/tools/combat' },
      { icon: FileText, label: 'Event Viewer', path: '/tools/events' },
    ],
  },
];

export function Sidebar() {
  const location = useLocation();

  return (
    <aside className="w-64 bg-card border-r border-border">
      <div className="p-4">
        <h1 className="text-xl font-bold">HeroScript</h1>
        <p className="text-sm text-muted-foreground">Content Editor</p>
      </div>
      <nav className="space-y-2 p-4">
        {menuItems.map((item, idx) =>
          'path' in item ? (
            <Link
              key={idx}
              to={item.path}
              className={`flex items-center gap-3 px-3 py-2 rounded-md hover:bg-accent ${
                location.pathname === item.path ? 'bg-accent' : ''
              }`}
            >
              <item.icon size={20} />
              <span>{item.label}</span>
            </Link>
          ) : (
            <div key={idx} className="space-y-1">
              <p className="px-3 py-2 text-sm font-semibold text-muted-foreground">
                {item.label}
              </p>
              {item.items?.map((subItem, subIdx) => (
                <Link
                  key={subIdx}
                  to={subItem.path}
                  className={`flex items-center gap-3 px-3 py-2 rounded-md hover:bg-accent ${
                    location.pathname === subItem.path ? 'bg-accent' : ''
                  }`}
                >
                  <subItem.icon size={18} />
                  <span>{subItem.label}</span>
                </Link>
              ))}
            </div>
          )
        )}
      </nav>
    </aside>
  );
}
```


**File:** `src/components/layout/Header.tsx`

```typescript
export function Header() {
  return (
    <header className="h-16 border-b border-border bg-card px-6 flex items-center justify-between">
      <div>
        <h2 className="text-lg font-semibold">Content Editor</h2>
      </div>
      <div className="flex items-center gap-4">
        {/* Future: User menu, theme toggle */}
      </div>
    </header>
  );
}
```

**File:** `src/components/layout/Footer.tsx`

```typescript
export function Footer() {
  return (
    <footer className="h-12 border-t border-border bg-card px-6 flex items-center justify-between text-sm text-muted-foreground">
      <div>HeroScript Engine v1.0</div>
      <div>API: localhost:5260</div>
    </footer>
  );
}
```

### 2.2 React Router Setup (1-2 hours)

**File:** `src/routes.tsx`

```typescript
import { createBrowserRouter } from 'react-router-dom';
import { Layout } from './components/layout/Layout';
import { Dashboard } from './pages/Dashboard';
import { ActionEditor } from './components/editors/ActionEditor';
import { EntityEditor } from './components/editors/EntityEditor';
import { StatusEditor } from './components/editors/StatusEditor';
import { GambitEditor } from './components/editors/GambitEditor';
import { FormulaTester } from './components/tools/FormulaTester';
import { CombatSimulator } from './components/tools/CombatSimulator';
import { EventViewer } from './components/tools/EventViewer';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <Dashboard /> },
      { path: 'actions', element: <ActionEditor /> },
      { path: 'entities', element: <EntityEditor /> },
      { path: 'status', element: <StatusEditor /> },
      { path: 'gambits', element: <GambitEditor /> },
      { path: 'tools/formula', element: <FormulaTester /> },
      { path: 'tools/combat', element: <CombatSimulator /> },
      { path: 'tools/events', element: <EventViewer /> },
    ],
  },
]);
```

**File:** `src/main.tsx`

```typescript
import React from 'react';
import ReactDOM from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { router } from './routes';
import './index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5000,
      refetchOnWindowFocus: true,
      retry: 1,
    },
  },
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>
);
```

---

## PHASE 3: CONTENT EDITORS (4-5 days)

This is the most complex phase. Each editor requires:
- List view with search/filters
- Create/Edit dialog with complex forms
- React Hook Form + Zod validation
- Delete confirmation
- Loading states + error handling

### 3.1 Action Editor (1-1.5 days)

**Recommended approach:** Start with a simple version, then iterate.

**Iteration 1: Basic CRUD (4-6 hours)**
- List view with table
- Simple create dialog (text fields only)
- Edit/Delete buttons
- Basic validation

**Iteration 2: Complex fields (4-6 hours)**
- Dynamic costs array
- Dynamic effects array
- Tags multi-select
- Full Zod validation
- JSON preview

**File structure:**
```
src/components/editors/ActionEditor/
+-- ActionEditor.tsx        # Main component
+-- ActionList.tsx          # Table view
+-- ActionDialog.tsx        # Create/Edit dialog
+-- ActionForm.tsx          # Form fields
+-- actionSchema.ts         # Zod validation
+-- types.ts                # Local types
```

**Key challenges:**
1. **Dynamic arrays:** Use React Hook Form's `useFieldArray`
2. **Complex nested objects:** Effects have multiple fields
3. **Validation:** Zod schema needs to match backend DTOs exactly

**Reference implementation:** See `docs/dashboard/roadmap.md` lines 353-390 for detailed UI specs

### 3.2 Entity Editor (1-1.5 days)

Similar structure to Action Editor but with:
- Nested Resources object (HP, Mana, Energy)
- Stats object (6 attributes)
- Optional Inventory object
- Conditional AI Config (only for ENEMY/BOSS types)

**Key challenges:**
1. **Conditional fields:** AI Config only shows for certain entity types
2. **Nested objects:** Resources and Stats are complex nested structures
3. **Type-specific validation:** Different validation rules per entity type

### 3.3 Status Effect Editor (0.5-1 day)

Simpler than Actions/Entities. Main challenge is the `effectsPerTick` array (similar to Action effects).

### 3.4 Gambit Editor (1-1.5 dias)

Most complex editor due to:
- **Dynamic conditions array:** Each condition has type-specific parameters
- **Conditional parameter inputs:** Parameters change based on condition type
- **Test functionality:** POST /api/gambits/decide integration

**Recommended approach:**
1. Start with fixed condition types
2. Add dynamic parameter inputs
3. Implement test dialog last

---

## PHASE 4: TESTING TOOLS (2-3 days)

### 4.1 Formula Tester (0.5-1 day)

Relatively straightforward. Main features:
- Monospace textarea for expression
- Dynamic variable inputs (key-value pairs)
- Result display with validation
- History (localStorage)
- Templates dropdown

**API:** `POST /api/math-expression/evaluate`

### 4.2 Combat Simulator (1.5-2 days)

**Most complex tool.** Requires state machine for 3 phases:

**Phase A: Setup**
- Hero selector (GET /api/entity/definitions?type=PLAYER)
- Enemies multi-select (GET /api/entity/definitions?type=ENEMY)
- Initial config form

**Phase B: Combat**
- Real-time state polling (GET /api/combat/{id} every 1s)
- Visual HP bars (progress bars with color gradients)
- Energy orbs (visual counter)
- Action grid (GET /api/combat/{id}/available-actions)
- Action log (scrollable list with auto-scroll)
- Turn controls

**Phase C: Result**
- Victory/Defeat modal
- Combat statistics
- Play again / Exit

**Key challenges:**
1. **State management:** Complex state machine with 3 phases
2. **Polling:** React Query with `refetchInterval`
3. **UI complexity:** Multiple visual components
4. **Action targeting:** Need targeting UI for multi-target actions

**Recommended approach:**
1. Build Setup phase first (simple form)
2. Build Result phase (simple modal)
3. Build Combat phase incrementally:
   - State display first (HP, energy)
   - Then action grid
   - Then action execution
   - Then AI turn
   - Then action log

### 4.3 Event Viewer (0.5-1 day)

Table with filters and export. Straightforward with Shadcn Table component.

**Features:**
- Pagination (use React Query `keepPreviousData`)
- Filters (query params)
- Export to JSON (`JSON.stringify` + download)
- Auto-refresh toggle (`refetchInterval` conditional)

---

## PHASE 5: POLISH & DEPLOY (1-2 days)

### 5.1 Error Handling & Loading States (3-4 hours)

**Tasks:**
- Add Skeleton components for all loading states
- Error boundaries (`react-error-boundary` library)
- React Query retry config (already configured in `main.tsx`)
- Toast notifications (Shadcn Sonner)
- Form validation messages

**File:** `src/components/ErrorBoundary.tsx`

```typescript
import { ErrorBoundary as ReactErrorBoundary } from 'react-error-boundary';

function ErrorFallback({ error, resetErrorBoundary }) {
  return (
    <div className="p-6">
      <h2>Something went wrong</h2>
      <pre className="text-sm">{error.message}</pre>
      <button onClick={resetErrorBoundary}>Try again</button>
    </div>
  );
}

export function ErrorBoundary({ children }) {
  return (
    <ReactErrorBoundary FallbackComponent={ErrorFallback}>
      {children}
    </ReactErrorBoundary>
  );
}
```

### 5.2 UX Polish (4-5 hours)

- CSS transitions (`transition-all duration-200`)
- Hover states (`hover:bg-accent`)
- Focus states (`focus:ring-2`)
- ARIA labels
- Keyboard navigation (Tab, Enter, Esc)
- Dark mode (optional, Shadcn supports it out-of-box)

### 5.3 Documentation (2-3 hours)

**File:** `tools/Dashboard/README.md`

```markdown
# HeroScript Content Editor Dashboard

Web-based dashboard for creating and editing game content.

## Quick Start

\\\ash
npm install
npm run dev
\\\

Dashboard will be available at http://localhost:5173

## Requirements

- Node.js 18+
- HeroScript API running at http://localhost:5260

## Development

\\\ash
npm run dev          # Start dev server
npm run build        # Build for production
npm run preview      # Preview production build
npm run lint         # Lint code
\\\

## Architecture

- **React 18** + TypeScript
- **Vite** for build tooling
- **TailwindCSS** + Shadcn/ui for styling
- **React Query** for server state
- **React Hook Form** + Zod for forms

## Adding New Editors

1. Create editor component in \src/components/editors/\
2. Add API service in \src/services/\
3. Add types in \src/types/\
4. Add React Query hooks in \src/hooks/\
5. Add route in \src/routes.tsx\
6. Add menu item in \src/components/layout/Sidebar.tsx\

## Troubleshooting

**CORS errors:** Ensure API CORS is configured for \http://localhost:5173\

**API not found:** Check API is running at \http://localhost:5260\

**Build errors:** Clear node_modules and reinstall
\\\

### 5.4 Build & Deploy (2-3 hours)

**Production build:**
```bash
npm run build
```

**Test preview:**
```bash
npm run preview
```

**Deployment options:**

1. **Serve locally (MVP):**
   ```bash
   npm run preview  # Serves dist/ at localhost:4173
   ```

2. **Static hosting (Vercel/Netlify):**
   - Connect GitHub repo
   - Set build command: `npm run build`
   - Set output directory: `dist`
   - Deploy automatically on push

3. **Integrated with API:**
   - Build frontend: `npm run build`
   - Copy `dist/` to `src/API/wwwroot/`
   - API serves static files
   - Single port deployment

---

## ACCEPTANCE CRITERIA

### Phase 1-2 (Setup + Layout)
- ? Dashboard SPA running at `localhost:5173`
- ? Navigation between all screens functional
- ? API client connecting to backend at `localhost:5260`
- ? React Query caching working

### Phase 3 (Content Editors)
- ? Action Editor: CRUD complete, validation functional
- ? Entity Editor: CRUD complete, validation functional
- ? Status Editor: CRUD complete, validation functional
- ? Gambit Editor: CRUD complete, gambit test functional
- ? Loading states and error handling on all screens
- ? Toast notifications for success/error

### Phase 4 (Testing Tools)
- ? Formula Tester evaluates expressions correctly
- ? Combat Simulator: Setup ? Combat ? Victory/Defeat flow functional
- ? Combat Simulator: Responsive UI (HP bars, energy counter)
- ? Combat Simulator: Detailed action log
- ? Combat Simulator: AI turn functional
- ? Event Viewer: Filters work, pagination functional, export to JSON functional

### Phase 5 (Polish)
- ? Robust error handling throughout dashboard
- ? Polished UX (animations, visual feedback)
- ? Complete documentation (README + inline)
- ? Functional production build

---

## RISKS & MITIGATION

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| **Complex form validation** | HIGH | MEDIUM | Start with simple Zod schemas, iterate. Use React Hook Form Field Arrays. Test validation in isolation. |
| **Combat Simulator state complexity** | MEDIUM | HIGH | Simple polling (1s) for MVP, not WebSocket. Use React Query for auto-refetch. Separate UI state from game state. |
| **API response format mismatch** | MEDIUM | MEDIUM | Test all endpoints manually with Postman first. Create TypeScript types from C# DTOs. Add runtime validation with Zod if needed. |
| **Performance issues with large lists** | LOW | MEDIUM | Implement pagination if needed. Virtual scrolling (react-virtual) if lists grow. Debounce search/filters (300ms). |
| **CORS issues in development** | LOW | LOW | Backend already has CORS configured for `localhost:5173`. Document how to add new origins if needed. |

---

## SUCCESS METRICS

### MVP (Phases 1-3)
- ? Game designer creates 5 actions in <30min
- ? Game designer creates 3 enemies in <20min
- ? Zero crashes or data loss in 1 week of use

### Testing Tools (Phase 4)
- ? Combat simulator runs 10 combats without bugs
- ? Formula tester validates expressions correctly (100% accuracy)

### Production Ready (Phase 5)
- ? Dashboard loads in <3s
- ? All CRUD operations respond in <500ms (localhost)
- ? No console errors in production build

---

## IMPLEMENTATION SEQUENCE RECOMMENDATION

**Week 1:**
- Day 1: Phase 1 (Setup)
- Day 2-3: Phase 2 (Layout + Navigation)
- Day 4-5: Start Phase 3 (Action Editor)

**Week 2:**
- Day 1-2: Complete Phase 3 (Entity, Status, Gambit Editors)
- Day 3-4: Phase 4 (Testing Tools)
- Day 5: Phase 5 (Polish + Deploy)

**Incremental delivery:**
- After Day 5: Action Editor usable (can create/edit actions)
- After Week 1: All content editors usable
- After Week 2: Complete dashboard with testing tools

---

## NEXT STEPS

**To begin implementation:**

1. **Create project:**
   ```bash
   cd tools
   npm create vite@latest Dashboard -- --template react-ts
   cd Dashboard
   npm install
   ```

2. **Install dependencies:**
   ```bash
   npm install axios react-router-dom zustand @tanstack/react-query
   npm install react-hook-form zod @hookform/resolvers lucide-react
   npm install -D tailwindcss postcss autoprefixer
   npx tailwindcss init -p
   npx shadcn-ui@latest init
   ```

3. **Start backend API:**
   ```bash
   cd ../../src/API
   dotnet run
   ```

4. **Start dashboard:**
   ```bash
   cd ../../tools/Dashboard
   npm run dev
   ```

5. **Open browser:** http://localhost:5173

**Ready to start?** Let me know if you want me to:
- Begin implementation (Phase 1)
- Create detailed step-by-step guide for specific phase
- Answer questions about architecture/approach

---

**Document version:** 2.0  
**Last updated:** 2026-07-12  
**Status:** Ready for execution  
**Estimated timeline:** 8-12 days
