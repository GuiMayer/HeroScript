import type { StatusEffect } from '@/types/status';
import type { StatusEffectFormData } from './statusEffectSchema';

export interface StatusEffectEditorProps {
  onClose?: () => void;
}

export interface StatusEffectListProps {
  onEdit: (effect: StatusEffect) => void;
  onDelete: (effectId: string) => void;
  onCreate: () => void;
}

export interface StatusEffectDialogProps {
  open: boolean;
  onClose: () => void;
  effect?: StatusEffect;
  mode: 'create' | 'edit';
}

export interface StatusEffectFormProps {
  effect?: StatusEffect;
  onSubmit: (data: StatusEffectFormData) => void;
  onCancel: () => void;
  isLoading?: boolean;
}
