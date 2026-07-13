import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { GambitForm } from './GambitForm';
import { useCreateGambit, useUpdateGambit } from '@/hooks/useGambits';
import type { GambitDialogProps } from './types';

export function GambitDialog({ open, onClose, gambit, mode }: GambitDialogProps) {
  const createMutation = useCreateGambit();
  const updateMutation = useUpdateGambit();

  const handleSubmit = (data: any) => {
    if (mode === 'create') {
      createMutation.mutate(data, {
        onSuccess: () => {
          onClose();
        },
      });
    } else {
      updateMutation.mutate(
        { gambitId: gambit!.gambitId, gambit: data },
        {
          onSuccess: () => {
            onClose();
          },
        }
      );
    }
  };

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-4xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {mode === 'create' ? 'Create New Gambit' : 'Edit Gambit'}
          </DialogTitle>
        </DialogHeader>
        <GambitForm
          gambit={gambit}
          onSubmit={handleSubmit}
          onCancel={onClose}
          isLoading={createMutation.isPending || updateMutation.isPending}
        />
      </DialogContent>
    </Dialog>
  );
}
