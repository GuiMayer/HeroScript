import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { gambitService } from '@/services/gambitService';
import type { 
  Gambit, 
  CreateGambitDto, 
  UpdateGambitDto,
  GambitDecisionRequest 
} from '@/types/gambit';

const QUERY_KEY = 'gambits';

// Get all gambits
export const useGambits = () => {
  return useQuery({
    queryKey: [QUERY_KEY],
    queryFn: gambitService.getAll,
  });
};

// Get single gambit
export const useGambit = (gambitId: string) => {
  return useQuery({
    queryKey: [QUERY_KEY, gambitId],
    queryFn: () => gambitService.getById(gambitId),
    enabled: !!gambitId,
  });
};

// Create gambit
export const useCreateGambit = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (gambit: CreateGambitDto) => gambitService.create(gambit),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Update gambit
export const useUpdateGambit = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ gambitId, gambit }: { gambitId: string; gambit: UpdateGambitDto }) =>
      gambitService.update(gambitId, gambit),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Delete gambit
export const useDeleteGambit = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (gambitId: string) => gambitService.delete(gambitId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Get decision
export const useGambitDecision = () => {
  return useMutation({
    mutationFn: (request: GambitDecisionRequest) => gambitService.getDecision(request),
  });
};
