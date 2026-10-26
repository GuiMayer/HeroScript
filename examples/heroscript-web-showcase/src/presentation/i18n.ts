import { deepClone, isPlainObject, JsonObject, Signal } from "../contracts/types";
import enCatalog from "../data/locales/en.json";
import ptBrCatalog from "../data/locales/pt_BR.json";
import contentCatalog from "../data/locales/content.json";
import basePresentation from "../data/presentation.json";
import volatilePresentation from "../data/volatile_core_presentation.json";
import worldOnePresentation from "../data/world_one_presentation.json";

function sprintf(template: string, args: unknown[]): string {
  if (!args || args.length === 0) return template;
  let index = 0;
  return template.replace(/%[sd]/g, () => {
    const val = index < args.length ? args[index++] : "";
    return String(val ?? "");
  });
}

export function formatPrintf(template: string, argOrArgs: unknown): string {
  const args = Array.isArray(argOrArgs) ? argOrArgs : [argOrArgs];
  return sprintf(template, args);
}

export class I18n {
  static readonly FALLBACK_LOCALE = "en";
  static readonly LOCALES = ["en", "pt_BR"] as const;

  readonly localeChanged = new Signal<void>();
  locale: string = I18n.FALLBACK_LOCALE;
  catalogs: Record<string, Record<string, string>> = {
    en: enCatalog as Record<string, string>,
    pt_BR: ptBrCatalog as Record<string, string>,
  };
  contentNames: Record<string, Record<string, string>> = contentCatalog as Record<string, Record<string, string>>;

  constructor(initialLocale = I18n.FALLBACK_LOCALE) {
    this.setLocale(initialLocale);
  }

  setLocale(code: string): void {
    const nextLocale = (I18n.LOCALES as readonly string[]).includes(code) ? code : I18n.FALLBACK_LOCALE;
    if (this.locale === nextLocale) {
      return;
    }
    this.locale = nextLocale;
    this.localeChanged.emit();
  }

  text(key: string, ...args: unknown[]): string {
    const currentCatalog = this.catalogs[this.locale] ?? {};
    const fallbackCatalog = this.catalogs[I18n.FALLBACK_LOCALE] ?? {};
    const translated = String(currentCatalog[key] ?? "");
    const resolved = translated.length > 0 ? translated : String(fallbackCatalog[key] ?? "") || key;
    if (args.length === 0) return resolved;
    if (args.length === 1 && Array.isArray(args[0])) {
      return sprintf(resolved, args[0]);
    }
    return sprintf(resolved, args);
  }

  number(value: number): string {
    const num = Number(value);
    if (!Number.isFinite(num)) return "0";
    const rounded = Math.round(num);
    const formatted = Math.abs(num - rounded) < 1e-5 ? String(rounded) : String(Math.round(num * 100) / 100);
    return this.locale === "pt_BR" ? formatted.replace(".", ",") : formatted;
  }

  contentName(id: string, fallback = ""): string {
    const names = this.contentNames[id] ?? {};
    const translated = String(names[this.locale] ?? "");
    if (translated.length > 0) return translated;
    const english = String(names[I18n.FALLBACK_LOCALE] ?? "");
    if (english.length > 0) return english;
    if (fallback.length > 0) return fallback;
    return id
      .replace(/_/g, " ")
      .replace(/\b\w/g, (char) => char.toUpperCase());
  }

  presentation(source: JsonObject): JsonObject {
    const result = deepClone(source);
    for (const kind of ["cards", "nodes"]) {
      const group = result[kind];
      if (isPlainObject(group)) {
        for (const id of Object.keys(group)) {
          const item = group[id];
          if (isPlainObject(item)) {
            for (const field of ["name", "text", "hint"]) {
              if (field in item) {
                item[field] = this.text(String(item[field] ?? ""));
              }
            }
          }
        }
      }
    }
    const sandboxModes = result.sandbox_modes;
    if (Array.isArray(sandboxModes)) {
      for (const mode of sandboxModes) {
        if (isPlainObject(mode)) {
          mode.name = this.text(String(mode.name ?? ""));
          mode.description = this.text(String(mode.description ?? ""));
        }
      }
    }
    return result;
  }

  error(result: JsonObject): string {
    const key = String(result.errorKey ?? "");
    if (!key) {
      return String(result.error ?? "Unknown engine error.");
    }
    const args = Array.isArray(result.errorArgs) ? result.errorArgs : [];
    return args.length > 0 ? sprintf(this.text(key), args) : this.text(key);
  }

  static buildMergedPresentation(): JsonObject {
    const merged = deepClone(basePresentation as unknown as JsonObject);
    for (const overlay of [
      volatilePresentation as unknown as JsonObject,
      worldOnePresentation as unknown as JsonObject,
    ]) {
      for (const category of [
        "cards",
        "nodes",
        "conditions",
        "card_tag_labels",
        "card_flow_labels",
        "condensation_labels",
        "intent_icons",
      ]) {
        if (!isPlainObject(merged[category])) {
          merged[category] = {};
        }
        const target = merged[category] as JsonObject;
        const sourceCategory = overlay[category];
        if (isPlainObject(sourceCategory)) {
          Object.assign(target, deepClone(sourceCategory));
        }
      }
    }
    return merged;
  }
}
