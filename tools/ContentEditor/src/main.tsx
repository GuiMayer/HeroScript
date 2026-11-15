import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import '@fontsource-variable/dm-sans'
import '@fontsource-variable/geist'
import './styles.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, staleTime: 15_000 } },
})

createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={queryClient}><App /></QueryClientProvider>,
)
