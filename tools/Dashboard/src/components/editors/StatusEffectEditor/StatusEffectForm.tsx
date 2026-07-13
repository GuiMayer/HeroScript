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
import { statusEffectSchema, type StatusEffectFormData } from './statusEffectSchema';
import type { StatusEffectFormProps } from './types';
import { StatusEffectType } from '@/types/status';

export function StatusEffectForm({ effect, onSubmit, onCancel, isLoading }: StatusEffectFormProps) {
  const [showPreview, setShowPreview] = useState(false);
  const [tagInput, setTagInput] = useState('');

  const {
    register,
    control,
    handleSubmit,
    watch,
    setValue,
    formState: { errors },
  } = useForm<StatusEffectFormData>({
    resolver: zodResolver(statusEffectSchema),
    defaultValues: effect || {
      statusId: '',
      displayName: '',
      description: '',
      statusType: StatusEffectType.BUFF,
      duration: 3,
      stackable: false,
      maxStacks: 1,
      effectsPerTick: [],
      tags: [],
    },
  });

  const {
    fields: effectFields,
    append: appendEffect,
    remove: removeEffect,
  } = useFieldArray({
    control,
    name: 'effectsPerTick',
  });

  const watchedValues = watch();
  const tags = watch('tags') || [];

  const handleAddEffect = () => {
    appendEffect({
      effectId: '',
      type: '',
      value: 0,
    });
  };

  const handleAddTag = () => {
    if (tagInput.trim() && !tags.includes(tagInput.trim())) {
      setValue('tags', [...tags, tagInput.trim()]);
      setTagInput('');
    }
  };

  const handleRemoveTag = (tagToRemove: string) => {
    setValue(
      'tags',
      tags.filter((tag: string) => tag !== tagToRemove)
    );
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
      {/* Basic Fields */}
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="statusId">Status ID *</Label>
          <Input
            id="statusId"
            {...register('statusId')}
            placeholder="STATUS_NAME"
            disabled={!!effect}
          />
          {errors.statusId && (
            <p className="text-sm text-red-500">{errors.statusId.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="displayName">Display Name *</Label>
          <Input
            id="displayName"
            {...register('displayName')}
            placeholder="Status Name"
          />
          {errors.displayName && (
            <p className="text-sm text-red-500">{errors.displayName.message}</p>
          )}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description *</Label>
        <Textarea
          id="description"
          {...register('description')}
          placeholder="Describe what this status effect does..."
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-red-500">{errors.description.message}</p>
        )}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="statusType">Status Type *</Label>
          <Select
            value={watchedValues.statusType}
            onValueChange={(value) => setValue('statusType', value as StatusEffectType)}
          >
            <SelectTrigger>
              <SelectValue placeholder="Select type" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={StatusEffectType.BUFF}>Buff</SelectItem>
              <SelectItem value={StatusEffectType.DEBUFF}>Debuff</SelectItem>
              <SelectItem value={StatusEffectType.DOT}>Damage Over Time</SelectItem>
              <SelectItem value={StatusEffectType.HOT}>Heal Over Time</SelectItem>
              <SelectItem value={StatusEffectType.STUN}>Stun</SelectItem>
              <SelectItem value={StatusEffectType.SHIELD}>Shield</SelectItem>
            </SelectContent>
          </Select>
          {errors.statusType && (
            <p className="text-sm text-red-500">{errors.statusType.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="duration">Duration (turns) *</Label>
          <Input
            id="duration"
            type="number"
            {...register('duration', { valueAsNumber: true })}
            placeholder="-1 for permanent"
          />
          {errors.duration && (
            <p className="text-sm text-red-500">{errors.duration.message}</p>
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="flex items-center space-x-2">
          <Checkbox
            id="stackable"
            checked={watchedValues.stackable}
            onCheckedChange={(checked) => setValue('stackable', checked as boolean)}
          />
          <Label htmlFor="stackable">Stackable</Label>
        </div>

        {watchedValues.stackable && (
          <div className="space-y-2">
            <Label htmlFor="maxStacks">Max Stacks *</Label>
            <Input
              id="maxStacks"
              type="number"
              {...register('maxStacks', { valueAsNumber: true })}
              placeholder="Maximum stack count"
            />
            {errors.maxStacks && (
              <p className="text-sm text-red-500">{errors.maxStacks.message}</p>
            )}
          </div>
        )}
      </div>

      {/* Effects Per Tick */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="text-lg">Effects Per Tick</CardTitle>
            <Button type="button" onClick={handleAddEffect} size="sm">
              <Plus className="h-4 w-4 mr-1" />
              Add Effect
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {effectFields.length === 0 && (
            <p className="text-sm text-gray-500 text-center py-4">
              No effects per tick. Click "Add Effect" to add one.
            </p>
          )}

          {effectFields.map((field, index) => (
            <Card key={field.id}>
              <CardContent className="pt-4">
                <div className="grid grid-cols-12 gap-4">
                  <div className="col-span-4 space-y-2">
                    <Label htmlFor={`effectsPerTick.${index}.effectId`}>Effect ID *</Label>
                    <Input
                      {...register(`effectsPerTick.${index}.effectId`)}
                      placeholder="EFFECT_ID"
                    />
                    {errors.effectsPerTick?.[index]?.effectId && (
                      <p className="text-sm text-red-500">
                        {errors.effectsPerTick[index]?.effectId?.message}
                      </p>
                    )}
                  </div>

                  <div className="col-span-3 space-y-2">
                    <Label htmlFor={`effectsPerTick.${index}.type`}>Type *</Label>
                    <Input
                      {...register(`effectsPerTick.${index}.type`)}
                      placeholder="DAMAGE, HEAL, etc."
                    />
                    {errors.effectsPerTick?.[index]?.type && (
                      <p className="text-sm text-red-500">
                        {errors.effectsPerTick[index]?.type?.message}
                      </p>
                    )}
                  </div>

                  <div className="col-span-3 space-y-2">
                    <Label htmlFor={`effectsPerTick.${index}.value`}>Value *</Label>
                    <Input
                      type="number"
                      {...register(`effectsPerTick.${index}.value`, {
                        valueAsNumber: true,
                      })}
                      placeholder="0"
                    />
                  </div>

                  <div className="col-span-2 flex items-end justify-end">
                    <Button
                      type="button"
                      variant="destructive"
                      size="sm"
                      onClick={(e: React.MouseEvent) => {
                        e.preventDefault();
                        removeEffect(index);
                      }}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </CardContent>
      </Card>

      {/* Tags */}
      <div className="space-y-2">
        <Label>Tags</Label>
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
          <Button type="button" onClick={handleAddTag}>
            Add
          </Button>
        </div>
        <div className="flex flex-wrap gap-2 mt-2">
          {tags.map((tag: string) => (
            <span
              key={tag}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-gray-100 text-sm"
            >
              {tag}
              <button
                type="button"
                onClick={() => handleRemoveTag(tag)}
                className="text-red-500 hover:text-red-700"
              >
                ×
              </button>
            </span>
          ))}
        </div>
      </div>

      {/* Preview Toggle */}
      <Button
        type="button"
        variant="outline"
        onClick={() => setShowPreview(!showPreview)}
        className="w-full"
      >
        {showPreview ? <ChevronUp className="h-4 w-4 mr-2" /> : <ChevronDown className="h-4 w-4 mr-2" />}
        {showPreview ? 'Hide' : 'Show'} Preview
      </Button>

      {/* Preview */}
      {showPreview && (
        <Card>
          <CardHeader>
            <CardTitle>Effect Preview</CardTitle>
          </CardHeader>
          <CardContent>
            <pre className="text-sm overflow-auto">
              {JSON.stringify(watchedValues, null, 2)}
            </pre>
          </CardContent>
        </Card>
      )}

      {/* Form Actions */}
      <div className="flex gap-2 justify-end">
        <Button type="button" variant="outline" onClick={onCancel} disabled={isLoading}>
          Cancel
        </Button>
        <Button type="submit" disabled={isLoading}>
          {isLoading ? 'Saving...' : effect ? 'Update Effect' : 'Create Effect'}
        </Button>
      </div>
    </form>
  );
}
