import { useState } from 'react';
import { GambitList } from './GambitList';
import { GambitDialog } from './GambitDialog';
import type { Gambit } from '@/types/gambit';
import type { GambitEditorProps } from './types';

export function GambitEditor({ onClose }: GambitEditorProps) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedGambit, setSelectedGambit] = useState<Gambit | undefined>();
  const [mode, setMode] = useState<'create' | 'edit'>('create');

  const handleCreate = () => {
    setSelectedGambit(undefined);
    setMode('create');
    setDialogOpen(true);
  };

  const handleEdit = (gambit: Gambit) => {
    setSelectedGambit(gambit);
    setMode('edit');
    setDialogOpen(true);
  };

  const handleDelete = (gambitId: string) => {
    console.log('Deleted gambit:', gambitId);
  };

  const handleCloseDialog = () => {
    setDialogOpen(false);
    setSelectedGambit(undefined);
  };

  return (
    <div className="space-y-4">
      <GambitList
        onEdit={handleEdit}
        onDelete={handleDelete}
        onCreate={handleCreate}
      />
      <GambitDialog
        open={dialogOpen}
        onClose={handleCloseDialog}
        gambit={selectedGambit}
        mode={mode}
      />
    </div>
  );
}
