import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { StatusEffectForm } from './StatusEffectForm';
import { useCreateStatusEffect, useUpdateStatusEffect } from '@/hooks/useStatusEffects';
import type { StatusEffectDialogProps } from './types';

export function StatusEffectDialog({ open, onClose, effect, mode }: StatusEffectDialogProps) {
  const createMutation = useCreateStatusEffect();
  const updateMutation = useUpdateStatusEffect();

  const handleSubmit = (data: any) => {
    if (mode === 'create') {
      createMutation.mutate(data, {
        onSuccess: () => {
          onClose();
        },
      });
    } else {
      updateMutation.mutate(
        { statusId: effect!.statusId, status: data },
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
            {mode === 'create' ? 'Create New Status Effect' : 'Edit Status Effect'}
          </DialogTitle>
        </DialogHeader>
        <StatusEffectForm
          effect={effect}
          onSubmit={handleSubmit}
          onCancel={onClose}
          isLoading={createMutation.isPending || updateMutation.isPending}
        />
      </DialogContent>
    </Dialog>
  );
}
