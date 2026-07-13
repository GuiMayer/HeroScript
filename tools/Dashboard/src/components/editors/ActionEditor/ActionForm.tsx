import { useForm, useFieldArray } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2, ChevronDown, ChevronUp } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Checkbox } from '@/components/ui/checkbox';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { actionSchema, type ActionFormData } from './actionSchema';
import type { ActionFormProps } from './types';
import { ActionType } from '@/types/action';

export function ActionForm({ action, onSubmit, onCancel, isLoading }: ActionFormProps) {
  const [showPreview, setShowPreview] = useState(false);
  const [tagInput, setTagInput] = useState('');

  const {
    register,
    control,
    handleSubmit,
    watch,
    setValue,
    formState: { errors },
  } = useForm<ActionFormData>({
    resolver: zodResolver(actionSchema),
    defaultValues: action || {
      actionId: '',
      displayName: '',
      description: '',
      actionType: ActionType.ATTACK,
      costs: { resources: [] },
      effects: [],
      requiresTarget: true,
      multiTarget: false,
      cooldown: 0,
      tags: [],
    },
  });

  const {
    fields: costFields,
    append: appendCost,
    remove: removeCost,
  } = useFieldArray({
    control,
    name: 'costs.resources',
  });

  const {
    fields: effectFields,
    append: appendEffect,
    remove: removeEffect,
  } = useFieldArray({
    control,
    name: 'effects',
  });

  const formValues = watch();
  const tags = watch('tags') || [];

  const handleAddTag = () => {
    if (tagInput.trim() && !tags.includes(tagInput.trim())) {
      setValue('tags', [...tags, tagInput.trim()]);
      setTagInput('');
    }
  };

  const handleRemoveTag = (tagToRemove: string) => {
    setValue(
      'tags',
      tags.filter((tag) => tag !== tagToRemove)
    );
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
      {/* Basic Fields */}
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="actionId">Action ID *</Label>
          <Input
            id="actionId"
            {...register('actionId')}
            placeholder="FIREBALL"
            disabled={!!action}
            className="font-mono"
          />
          {errors.actionId && (
            <p className="text-sm text-destructive">{errors.actionId.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="displayName">Display Name *</Label>
          <Input
            id="displayName"
            {...register('displayName')}
            placeholder="Fireball"
          />
          {errors.displayName && (
            <p className="text-sm text-destructive">{errors.displayName.message}</p>
          )}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description *</Label>
        <Textarea
          id="description"
          {...register('description')}
          placeholder="A powerful fire spell that deals damage to enemies..."
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-destructive">{errors.description.message}</p>
        )}
      </div>

      <div className="grid grid-cols-3 gap-4">
        <div className="space-y-2">
          <Label htmlFor="actionType">Action Type *</Label>
          <Select
            value={formValues.actionType}
            onValueChange={(value) => setValue('actionType', value as any)}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="ATTACK">Attack</SelectItem>
              <SelectItem value="DEFENSE">Defense</SelectItem>
              <SelectItem value="UTILITY">Utility</SelectItem>
              <SelectItem value="SPELL">Spell</SelectItem>
            </SelectContent>
          </Select>
          {errors.actionType && (
            <p className="text-sm text-destructive">{errors.actionType.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="cooldown">Cooldown (turns)</Label>
          <Input
            id="cooldown"
            type="number"
            {...register('cooldown', { valueAsNumber: true })}
            placeholder="0"
          />
          {errors.cooldown && (
            <p className="text-sm text-destructive">{errors.cooldown.message}</p>
          )}
        </div>

        <div className="space-y-4 pt-7">
          <div className="flex items-center space-x-2">
            <Checkbox
              id="requiresTarget"
              checked={formValues.requiresTarget}
              onCheckedChange={(checked) => setValue('requiresTarget', !!checked)}
            />
            <Label htmlFor="requiresTarget" className="text-sm font-normal">
              Requires Target
            </Label>
          </div>
          <div className="flex items-center space-x-2">
            <Checkbox
              id="multiTarget"
              checked={formValues.multiTarget}
              onCheckedChange={(checked) => setValue('multiTarget', !!checked)}
            />
            <Label htmlFor="multiTarget" className="text-sm font-normal">
              Multi Target
            </Label>
          </div>
        </div>
      </div>

      {/* Resource Costs */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base flex items-center justify-between">
            Resource Costs
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => appendCost({ resourceId: '', amount: 0 })}
            >
              <Plus className="h-4 w-4 mr-1" />
              Add Cost
            </Button>
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {costFields.length === 0 ? (
            <p className="text-sm text-muted-foreground">No costs defined</p>
          ) : (
            costFields.map((field, index) => (
              <div key={field.id} className="flex gap-2 items-start">
                <div className="flex-1 space-y-2">
                  <Input
                    {...register(`costs.resources.${index}.resourceId`)}
                    placeholder="Resource ID (e.g., mana)"
                  />
                  {errors.costs?.resources?.[index]?.resourceId && (
                    <p className="text-xs text-destructive">
                      {errors.costs.resources[index]?.resourceId?.message}
                    </p>
                  )}
                </div>
                <div className="w-32 space-y-2">
                  <Input
                    type="number"
                    {...register(`costs.resources.${index}.amount`, {
                      valueAsNumber: true,
                    })}
                    placeholder="Amount"
                  />
                  {errors.costs?.resources?.[index]?.amount && (
                    <p className="text-xs text-destructive">
                      {errors.costs.resources[index]?.amount?.message}
                    </p>
                  )}
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => removeCost(index)}
                >
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
              </div>
            ))
          )}
        </CardContent>
      </Card>

      {/* Effects */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base flex items-center justify-between">
            Effects *
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                appendEffect({
                  effectId: '',
                  type: '',
                  target: '',
                  value: 0,
                  timing: '',
                })
              }
            >
              <Plus className="h-4 w-4 mr-1" />
              Add Effect
            </Button>
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {effectFields.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              At least one effect is required
            </p>
          ) : (
            effectFields.map((field, index) => (
              <div key={field.id} className="border rounded-lg p-4 space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-sm font-medium">Effect {index + 1}</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => removeEffect(index)}
                  >
                    <Trash2 className="h-4 w-4 text-destructive" />
                  </Button>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-2">
                    <Label>Effect ID</Label>
                    <Input
                      {...register(`effects.${index}.effectId`)}
                      placeholder="damage"
                    />
                    {errors.effects?.[index]?.effectId && (
                      <p className="text-xs text-destructive">
                        {errors.effects[index]?.effectId?.message}
                      </p>
                    )}
                  </div>
                  <div className="space-y-2">
                    <Label>Type</Label>
                    <Input
                      {...register(`effects.${index}.type`)}
                      placeholder="DAMAGE"
                    />
                    {errors.effects?.[index]?.type && (
                      <p className="text-xs text-destructive">
                        {errors.effects[index]?.type?.message}
                      </p>
                    )}
                  </div>
                  <div className="space-y-2">
                    <Label>Target</Label>
                    <Input
                      {...register(`effects.${index}.target`)}
                      placeholder="ENEMY"
                    />
                    {errors.effects?.[index]?.target && (
                      <p className="text-xs text-destructive">
                        {errors.effects[index]?.target?.message}
                      </p>
                    )}
                  </div>
                  <div className="space-y-2">
                    <Label>Value</Label>
                    <Input
                      type="number"
                      {...register(`effects.${index}.value`, {
                        valueAsNumber: true,
                      })}
                      placeholder="10"
                    />
                    {errors.effects?.[index]?.value && (
                      <p className="text-xs text-destructive">
                        {errors.effects[index]?.value?.message}
                      </p>
                    )}
                  </div>
                  <div className="space-y-2 col-span-2">
                    <Label>Timing</Label>
                    <Input
                      {...register(`effects.${index}.timing`)}
                      placeholder="IMMEDIATE"
                    />
                    {errors.effects?.[index]?.timing && (
                      <p className="text-xs text-destructive">
                        {errors.effects[index]?.timing?.message}
                      </p>
                    )}
                  </div>
                </div>
              </div>
            ))
          )}
          {errors.effects && typeof errors.effects === 'object' && !Array.isArray(errors.effects) && (
            <p className="text-sm text-destructive">{(errors.effects as any).message}</p>
          )}
        </CardContent>
      </Card>

      {/* Tags */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Tags</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex gap-2">
            <Input
              value={tagInput}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setTagInput(e.target.value)}
              placeholder="Enter tag"
              onKeyDown={(e: React.KeyboardEvent<HTMLInputElement>) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  handleAddTag();
                }
              }}
            />
            <Button type="button" variant="outline" onClick={handleAddTag}>
              <Plus className="h-4 w-4" />
            </Button>
          </div>
          <div className="flex flex-wrap gap-2">
            {tags.map((tag) => (
              <div
                key={tag}
                className="bg-secondary text-secondary-foreground px-3 py-1 rounded-full text-sm flex items-center gap-2"
              >
                {tag}
                <button
                  type="button"
                  onClick={() => handleRemoveTag(tag)}
                  className="hover:text-destructive"
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {/* JSON Preview */}
      <Card>
        <CardHeader
          className="cursor-pointer hover:bg-accent"
          onClick={() => setShowPreview(!showPreview)}
        >
          <CardTitle className="text-base flex items-center justify-between">
            JSON Preview
            {showPreview ? (
              <ChevronUp className="h-4 w-4" />
            ) : (
              <ChevronDown className="h-4 w-4" />
            )}
          </CardTitle>
        </CardHeader>
        {showPreview && (
          <CardContent>
            <pre className="bg-muted p-4 rounded-lg overflow-x-auto text-xs">
              {JSON.stringify(formValues, null, 2)}
            </pre>
          </CardContent>
        )}
      </Card>

      {/* Form Actions */}
      <div className="flex justify-end gap-3 pt-4 border-t">
        <Button type="button" variant="outline" onClick={onCancel} disabled={isLoading}>
          Cancel
        </Button>
        <Button type="submit" disabled={isLoading}>
          {isLoading ? 'Saving...' : action ? 'Update Action' : 'Create Action'}
        </Button>
      </div>
    </form>
  );
}
