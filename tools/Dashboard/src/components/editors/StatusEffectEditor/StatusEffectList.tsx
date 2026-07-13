import { useState } from 'react';
import { Search, Plus, Edit, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { useStatusEffects, useDeleteStatusEffect } from '@/hooks/useStatusEffects';
import type { StatusEffectListProps } from './types';
import type { StatusEffect } from '@/types/status';
import { StatusEffectType } from '@/types/status';

export function StatusEffectList({ onEdit, onDelete, onCreate }: StatusEffectListProps) {
  const [searchTerm, setSearchTerm] = useState('');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [effectToDelete, setEffectToDelete] = useState<StatusEffect | null>(null);

  const { data: effects, isLoading, error } = useStatusEffects();
  const deleteMutation = useDeleteStatusEffect();

  const handleDeleteClick = (effect: StatusEffect) => {
    setEffectToDelete(effect);
    setDeleteDialogOpen(true);
  };

  const handleDeleteConfirm = () => {
    if (effectToDelete) {
      deleteMutation.mutate(effectToDelete.statusId, {
        onSuccess: () => {
          setDeleteDialogOpen(false);
          setEffectToDelete(null);
          onDelete(effectToDelete.statusId);
        },
      });
    }
  };

  const filteredEffects = effects?.filter((effect) => {
    const matchesSearch =
      effect.displayName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      effect.statusId.toLowerCase().includes(searchTerm.toLowerCase()) ||
      effect.description.toLowerCase().includes(searchTerm.toLowerCase());

    const matchesType =
      typeFilter === 'all' || effect.statusType === typeFilter;

    return matchesSearch && matchesType;
  });

  if (error) {
    return (
      <Card>
        <CardContent className="pt-6">
          <p className="text-red-500">Error loading status effects: {error.message}</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>Status Effects</CardTitle>
            <Button onClick={onCreate}>
              <Plus className="h-4 w-4 mr-2" />
              New Effect
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="flex gap-4 mb-4">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
              <Input
                placeholder="Search effects..."
                value={searchTerm}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearchTerm(e.target.value)}
                className="pl-10"
              />
            </div>
            <Select value={typeFilter} onValueChange={setTypeFilter}>
              <SelectTrigger className="w-[180px]">
                <SelectValue placeholder="Filter by type" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All Types</SelectItem>
                <SelectItem value={StatusEffectType.BUFF}>Buff</SelectItem>
                <SelectItem value={StatusEffectType.DEBUFF}>Debuff</SelectItem>
                <SelectItem value={StatusEffectType.DOT}>Damage Over Time</SelectItem>
                <SelectItem value={StatusEffectType.HOT}>Heal Over Time</SelectItem>
                <SelectItem value={StatusEffectType.STUN}>Stun</SelectItem>
                <SelectItem value={StatusEffectType.SHIELD}>Shield</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {isLoading ? (
            <div className="text-center py-8">Loading status effects...</div>
          ) : filteredEffects && filteredEffects.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Status ID</TableHead>
                  <TableHead>Display Name</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Duration</TableHead>
                  <TableHead>Effects</TableHead>
                  <TableHead>Tags</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredEffects.map((effect) => (
                  <TableRow key={effect.statusId}>
                    <TableCell className="font-mono text-sm">
                      {effect.statusId}
                    </TableCell>
                    <TableCell className="font-medium">{effect.displayName}</TableCell>
                    <TableCell>
                      <Badge
                        variant={
                          effect.statusType === StatusEffectType.BUFF
                            ? 'default'
                            : effect.statusType === StatusEffectType.DEBUFF
                            ? 'destructive'
                            : 'secondary'
                        }
                      >
                        {effect.statusType}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {effect.duration === -1 ? 'Permanent' : `${effect.duration} turns`}
                    </TableCell>
                    <TableCell>
                      {effect.effectsPerTick.length > 0 ? (
                        <div className="text-sm">
                          {effect.effectsPerTick.slice(0, 2).map((eff, idx) => (
                            <div key={idx}>
                              {eff.type}: {eff.value > 0 ? '+' : ''}
                              {eff.value}
                            </div>
                          ))}
                          {effect.effectsPerTick.length > 2 && (
                            <div className="text-gray-500">
                              +{effect.effectsPerTick.length - 2} more
                            </div>
                          )}
                        </div>
                      ) : (
                        <span className="text-gray-400">None</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {effect.tags.slice(0, 2).map((tag) => (
                          <Badge key={tag} variant="outline" className="text-xs">
                            {tag}
                          </Badge>
                        ))}
                        {effect.tags.length > 2 && (
                          <Badge variant="outline" className="text-xs">
                            +{effect.tags.length - 2}
                          </Badge>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex gap-2 justify-end">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => onEdit(effect)}
                        >
                          <Edit className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleDeleteClick(effect)}
                        >
                          <Trash2 className="h-4 w-4 text-red-500" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <div className="text-center py-8 text-gray-500">
              No status effects found. Create your first effect to get started.
            </div>
          )}
        </CardContent>
      </Card>

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Status Effect</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete "{effectToDelete?.displayName}"? This action
              cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDeleteConfirm}
              className="bg-red-500 hover:bg-red-600"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
