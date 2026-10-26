import { deepClone, isPlainObject, JsonObject } from '../contracts/types';

/**
 * Pure ordered beats. Shares privately-owned immutable traces, never reorders facts.
 * Port of scripts/presentation/presentation_planner.gd
 */
export class PresentationPlanner {
  static plan(
    engineFrames: any[],
    _policy?: any,
    _context?: any
  ): JsonObject[] & { frames: JsonObject[] } {
    const result = [] as unknown as JsonObject[] & { frames: JsonObject[] };
    Object.defineProperty(result, 'frames', {
      get() {
        return result;
      },
      enumerable: false,
    });
    if (!Array.isArray(engineFrames)) return result;

    for (let frameIndex = 0; frameIndex < engineFrames.length; frameIndex++) {
      if (!isPlainObject(engineFrames[frameIndex])) continue;
      const frame = deepClone(engineFrames[frameIndex]);
      for (const field of ['effectSteps', 'applications', 'cardZoneSteps']) {
        frame[field] = Array.isArray(frame[field])
          ? frame[field].filter((value: any) => isPlainObject(value))
          : [];
      }
      for (const application of frame.applications) {
        application.cardZoneSteps = Array.isArray(application.cardZoneSteps)
          ? application.cardZoneSteps.filter((value: any) => isPlainObject(value))
          : [];
      }

      const condensed: Record<string, boolean> = {};
      const parents: Record<string, string> = {};
      for (const step of frame.effectSteps ?? []) {
        const identity = isPlainObject(step.identity) ? step.identity : {};
        const procId = String(identity.procId ?? '');
        parents[procId] =
          identity.parentProcId !== null && identity.parentProcId !== undefined
            ? String(identity.parentProcId)
            : '';
        if (step.condensation !== null && step.condensation !== undefined && procId.length > 0) {
          condensed[procId] = true;
        }
      }

      const groups: Array<{
        key: string;
        applications: JsonObject[];
        condensed: boolean;
        procId: string;
      }> = [];

      for (const application of frame.applications ?? []) {
        const identity = isPlainObject(application.identity) ? application.identity : {};
        const procId = String(identity.procId ?? '');
        let root = procId;
        for (let depth = 0; depth < 32; depth++) {
          if (condensed[root] || !(root in parents) || !parents[root]) break;
          root = parents[root];
        }
        let key = condensed[root] ? `condense:${root}` : `proc:${procId}`;
        if (procId.length === 0) {
          key = `application:${groups.length}`;
        }
        // Only contiguous applications may share a beat. Never pull A2 past B.
        if (groups.length === 0 || groups[groups.length - 1].key !== key) {
          groups.push({
            key,
            applications: [],
            condensed: Boolean(condensed[root]),
            procId: root,
          });
        }
        groups[groups.length - 1].applications.push(application);
      }

      if (groups.length === 0) {
        groups.push({ key: 'transition', applications: [], condensed: false, procId: '' });
      }

      for (let groupIndex = 0; groupIndex < groups.length; groupIndex++) {
        const group = groups[groupIndex];
        const beat: JsonObject = { ...frame };
        beat.applications = group.applications;
        beat.presentationProcId = group.procId;
        beat.condensed = group.condensed;
        beat.baseFrameIndex = frameIndex;
        beat.beatId = `${frame.frameId ?? frameIndex}/${groupIndex}/${group.key}`;
        if (groupIndex !== groups.length - 1) {
          delete beat.stateAfter;
          beat.cardZoneSteps = [];
        }
        beat.hasVisibleFacts =
          group.applications.length > 0 || (beat.cardZoneSteps?.length ?? 0) > 0;
        result.push(beat);
      }
    }
    return result;
  }
}
