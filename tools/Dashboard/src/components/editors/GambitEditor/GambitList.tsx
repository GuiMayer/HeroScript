import { useState } from 'react';
import { Search, Plus, Edit, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
import { useGambits, useDeleteGambit } from '@/hooks/useGambits';
import type { GambitListProps } from './types';
import type { Gambit } from '@/types/gambit';

export function GambitList({ onEdit, onDelete, onCreate }: GambitListProps) {
  const [searchTerm, setSearchTerm] = useState('');
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [gambitToDelete, setGambitToDelete] = useState<Gambit | null>(null);

  const { data: gambits, isLoading, error } = useGambits();
  const deleteMutation = useDeleteGambit();

  const handleDeleteClick = (gambit: Gambit) => {
    setGambitToDelete(gambit);
    setDeleteDialogOpen(true);
  };

  const handleDeleteConfirm = () => {
    if (gambitToDelete) {
      deleteMutation.mutate(gambitToDelete.gambitId, {
        onSuccess: () => {
          setDeleteDialogOpen(false);
          setGambitToDelete(null);
          onDelete(gambitToDelete.gambitId);
        },
      });
    }
  };

  const filteredGambits = gambits?.filter((gambit) => {
    const matchesSearch =
      gambit.displayName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      gambit.gambitId.toLowerCase().includes(searchTerm.toLowerCase()) ||
      gambit.description.toLowerCase().includes(searchTerm.toLowerCase()) ||
      gambit.actionId.toLowerCase().includes(searchTerm.toLowerCase());

    return matchesSearch;
  });

  // Sort by priority (descending)
  const sortedGambits = filteredGambits?.sort((a, b) => b.priority - a.priority);

  if (error) {
    return (
      <Card>
        <CardContent className="pt-6">
          <p className="text-red-500">Error loading gambits: {error.message}</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>Gambits</CardTitle>
            <Button onClick={onCreate}>
              <Plus className="h-4 w-4 mr-2" />
              New Gambit
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="flex gap-4 mb-4">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
              <Input
                placeholder="Search gambits..."
                value={searchTerm}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearchTerm(e.target.value)}
                className="pl-10"
              />
            </div>
          </div>

          {isLoading ? (
            <div className="text-center py-8">Loading gambits...</div>
          ) : sortedGambits && sortedGambits.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Priority</TableHead>
                  <TableHead>Gambit ID</TableHead>
                  <TableHead>Display Name</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Conditions</TableHead>
                  <TableHead>Tags</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {sortedGambits.map((gambit) => (
                  <TableRow key={gambit.gambitId}>
                    <TableCell>
                      <Badge variant="outline">{gambit.priority}</Badge>
                    </TableCell>
                    <TableCell className="font-mono text-sm">
                      {gambit.gambitId}
                    </TableCell>
                    <TableCell className="font-medium">{gambit.displayName}</TableCell>
                    <TableCell>
                      <Badge variant="secondary">{gambit.actionId}</Badge>
                    </TableCell>
                    <TableCell>
                      <div className="text-sm">
                        {gambit.conditions.length > 0 ? (
                          <>
                            <div>{gambit.conditions[0].conditionType}</div>
                            {gambit.conditions.length > 1 && (
                              <div className="text-gray-500">
                                +{gambit.conditions.length - 1} more
                              </div>
                            )}
                          </>
                        ) : (
                          <span className="text-gray-400">None</span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {gambit.tags.slice(0, 2).map((tag) => (
                          <Badge key={tag} variant="outline" className="text-xs">
                            {tag}
                          </Badge>
                        ))}
                        {gambit.tags.length > 2 && (
                          <Badge variant="outline" className="text-xs">
                            +{gambit.tags.length - 2}
                          </Badge>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex gap-2 justify-end">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => onEdit(gambit)}
                        >
                          <Edit className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleDeleteClick(gambit)}
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
              No gambits found. Create your first gambit to get started.
            </div>
          )}
        </CardContent>
      </Card>

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Gambit</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete "{gambitToDelete?.displayName}"? This action
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
