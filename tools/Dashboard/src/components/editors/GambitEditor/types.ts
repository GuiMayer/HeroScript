import type { Gambit } from '@/types/gambit';
import type { GambitFormData } from './gambitSchema';

export interface GambitEditorProps {
  onClose?: () => void;
}

export interface GambitListProps {
  onEdit: (gambit: Gambit) => void;
  onDelete: (gambitId: string) => void;
  onCreate: () => void;
}

export interface GambitDialogProps {
  open: boolean;
  onClose: () => void;
  gambit?: Gambit;
  mode: 'create' | 'edit';
}

export interface GambitFormProps {
  gambit?: Gambit;
  onSubmit: (data: GambitFormData) => void;
  onCancel: () => void;
  isLoading?: boolean;
}
