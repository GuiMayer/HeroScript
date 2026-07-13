import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { combatService } from '@/services/combatService';
import type { 
  StartCombatRequest,
  ExecuteActionRequest
} from '@/types/combat';

const QUERY_KEY = 'combat';

// Get combat state
export const useCombatState = (combatId: string) => {
  return useQuery({
    queryKey: [QUERY_KEY, combatId, 'state'],
    queryFn: () => combatService.getState(combatId),
    enabled: !!combatId,
    refetchInterval: 2000, // Poll every 2 seconds during combat
  });
};

// Get available actions
export const useAvailableActions = (combatId: string, actorId: string) => {
  return useQuery({
    queryKey: [QUERY_KEY, combatId, 'actor', actorId, 'actions'],
    queryFn: () => combatService.getAvailableActions(combatId, actorId),
    enabled: !!combatId && !!actorId,
  });
};

// Start combat
export const useStartCombat = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (request: StartCombatRequest) => combatService.start(request),
    onSuccess: (data) => {
      queryClient.setQueryData([QUERY_KEY, data.combatId, 'state'], data.initialState);
    },
  });
};

// Execute action
export const useExecuteAction = (combatId: string, actorId: string) => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (request: ExecuteActionRequest) => 
      combatService.executeAction(combatId, actorId, request),
    onSuccess: (data) => {
      queryClient.setQueryData([QUERY_KEY, combatId, 'state'], data);
      queryClient.invalidateQueries({ 
        queryKey: [QUERY_KEY, combatId, 'actor', actorId, 'actions'] 
      });
    },
  });
};

// End turn
export const useEndTurn = (combatId: string, actorId: string) => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: () => combatService.endTurn(combatId, actorId),
    onSuccess: (data) => {
      queryClient.setQueryData([QUERY_KEY, combatId, 'state'], data);
    },
  });
};

// End combat
export const useEndCombat = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (combatId: string) => combatService.end(combatId),
    onSuccess: (_, combatId) => {
      queryClient.removeQueries({ queryKey: [QUERY_KEY, combatId] });
    },
  });
};
