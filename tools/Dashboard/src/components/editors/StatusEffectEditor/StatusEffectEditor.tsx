import { useState } from 'react';
import { StatusEffectList } from './StatusEffectList';
import { StatusEffectDialog } from './StatusEffectDialog';
import type { StatusEffect } from '@/types/status';
import type { StatusEffectEditorProps } from './types';

export function StatusEffectEditor({ onClose }: StatusEffectEditorProps) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedEffect, setSelectedEffect] = useState<StatusEffect | undefined>();
  const [mode, setMode] = useState<'create' | 'edit'>('create');

  const handleCreate = () => {
    setSelectedEffect(undefined);
    setMode('create');
    setDialogOpen(true);
  };

  const handleEdit = (effect: StatusEffect) => {
    setSelectedEffect(effect);
    setMode('edit');
    setDialogOpen(true);
  };

  const handleDelete = (effectId: string) => {
    console.log('Deleted effect:', effectId);
  };

  const handleCloseDialog = () => {
    setDialogOpen(false);
    setSelectedEffect(undefined);
  };

  return (
    <div className="space-y-4">
      <StatusEffectList
        onEdit={handleEdit}
        onDelete={handleDelete}
        onCreate={handleCreate}
      />
      <StatusEffectDialog
        open={dialogOpen}
        onClose={handleCloseDialog}
        effect={selectedEffect}
        mode={mode}
      />
    </div>
  );
}
