import { deepClone, deterministicHash, isPlainObject, JsonObject } from "../contracts/types";
import { ActivityPresenter } from "./activityPresenter";
import { I18n } from "./i18n";

/**
 * Converts engine dialogue projections to display data. No condition/effect evaluation or networking.
 */
export class DialoguePresenter {
  static localized(value: unknown, locale: string): string {
    if (!isPlainObject(value)) return "";
    const translations = isPlainObject(value.translations)
      ? value.translations
      : {};
    const translated = String(translations[locale] ?? "");
    return translated.length > 0 ? translated : String(value.text ?? "");
  }

  static build(
    run: JsonObject,
    choices: JsonObject[],
    translator: I18n
  ): JsonObject {
    let dialogue: JsonObject = {};
    let advertisedInstance = "";
    for (const choice of choices) {
      if (String(choice.type ?? "") === "CHOOSE_DIALOGUE_OPTION") {
        const payload = isPlainObject(choice.payload) ? choice.payload : {};
        advertisedInstance = String(payload.dialogueInstanceId ?? "");
        break;
      }
    }

    const dialogues = Array.isArray(run.dialogues)
      ? (run.dialogues as JsonObject[])
      : [];
    const currentNodeId = String(run.currentNodeId ?? "");
    for (const item of dialogues) {
      if (String(item.activityNodeId ?? "") !== currentNodeId) continue;
      if (
        advertisedInstance.length > 0 &&
        String(item.dialogueInstanceId ?? "") !== advertisedInstance
      ) {
        continue;
      }
      dialogue = item;
    }
    if (Object.keys(dialogue).length === 0) return {};

    const options: JsonObject[] = [];
    const rawChoices = Array.isArray(dialogue.choices)
      ? (dialogue.choices as JsonObject[])
      : [];
    for (const option of rawChoices) {
      let command: JsonObject = {};
      for (const choice of choices) {
        const payload = isPlainObject(choice.payload) ? choice.payload : {};
        if (
          String(choice.type ?? "") === "CHOOSE_DIALOGUE_OPTION" &&
          String(choice.subjectId ?? "") === String(option.choiceId ?? "") &&
          String(payload.dialogueInstanceId ?? "") ===
            String(dialogue.dialogueInstanceId ?? "") &&
          String(payload.nodeId ?? "") === String(dialogue.nodeId ?? "")
        ) {
          command = deepClone(choice);
        }
      }
      const price: string[] = [];
      const costs = Array.isArray(option.costs)
        ? (option.costs as JsonObject[])
        : [];
      for (const cost of costs) {
        price.push(
          `${translator.number(Number(cost.amount ?? 0))} ${translator.contentName(
            String(cost.resourceId ?? "")
          )}`
        );
      }
      const hasCommand = Object.keys(command).length > 0;
      options.push({
        id: option.choiceId,
        text: DialoguePresenter.localized(option.text, translator.locale),
        cost: price.join(" + "),
        available: Boolean(option.available) && hasCommand,
        reason: isPlainObject(option.unavailableText)
          ? DialoguePresenter.localized(option.unavailableText, translator.locale)
          : "",
        interactionKind: String(option.interactionKind ?? "Choice"),
        reference: hasCommand
          ? deterministicHash([
              run.runId ?? "",
              run.branchId ?? "",
              run.sequence ?? 0,
              dialogue.contentRevision ?? "",
              dialogue.dialogueInstanceId ?? "",
              dialogue.nodeId ?? "",
              ActivityPresenter.optionReference(command),
            ])
          : "",
        command,
      });
    }

    const history: JsonObject[] = [];
    const transcript = Array.isArray(dialogue.transcript)
      ? (dialogue.transcript as JsonObject[])
      : [];
    for (const entry of transcript) {
      history.push({
        speaker: DialoguePresenter.localized(entry.speaker, translator.locale),
        text: DialoguePresenter.localized(entry.text, translator.locale),
      });
    }

    return {
      runId: String(run.runId ?? ""),
      branchId: String(run.branchId ?? ""),
      sequence: run.sequence ?? 0,
      dialogueInstanceId: String(dialogue.dialogueInstanceId ?? ""),
      dialogueId: String(dialogue.dialogueId ?? ""),
      nodeId: String(dialogue.nodeId ?? ""),
      activityNodeId: String(dialogue.activityNodeId ?? ""),
      contentRevision: String(dialogue.contentRevision ?? ""),
      speakerId: String(dialogue.speakerId ?? ""),
      title: DialoguePresenter.localized(dialogue.title, translator.locale),
      speaker: DialoguePresenter.localized(dialogue.speaker, translator.locale),
      text: DialoguePresenter.localized(dialogue.text, translator.locale),
      portrait: String(dialogue.portraitId ?? ""),
      completed: Boolean(dialogue.completed),
      options,
      history,
    };
  }

  static recordedTranscript(state: JsonObject, translator: I18n): string[] {
    const lines: string[] = [];
    const narrative = isPlainObject(state.narrative) ? state.narrative : {};
    const dialogues = Array.isArray(narrative.dialogues)
      ? (narrative.dialogues as JsonObject[])
      : [];
    const currentNodeId = String(state.currentNodeId ?? "");

    for (const dialogue of dialogues) {
      if (String(dialogue.activityNodeId ?? "") !== currentNodeId) continue;
      const definition = isPlainObject(dialogue.definition)
        ? dialogue.definition
        : {};
      const nodes = Array.isArray(definition.nodes)
        ? (definition.nodes as JsonObject[])
        : [];
      const history = Array.isArray(dialogue.history)
        ? (dialogue.history as JsonObject[])
        : [];

      for (const visit of history) {
        for (const node of nodes) {
          if (String(node.nodeId ?? "") !== String(visit.nodeId ?? "")) continue;
          if (visit.choiceId == null) {
            lines.push(
              `${DialoguePresenter.localized(
                node.speaker ?? {},
                translator.locale
              )}: ${DialoguePresenter.localized(node.text ?? {}, translator.locale)}`
            );
          } else {
            const choices = Array.isArray(node.choices)
              ? (node.choices as JsonObject[])
              : [];
            for (const choice of choices) {
              if (String(choice.choiceId ?? "") === String(visit.choiceId)) {
                lines.push(
                  `› ${DialoguePresenter.localized(
                    choice.text ?? {},
                    translator.locale
                  )}`
                );
              }
            }
          }
        }
      }
    }
    return lines;
  }
}
