import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SettingsScreen } from "../../src/ui/screens/ArchiveAndSettingsScreens";
import {
  CardHandLabScreen,
  InscriptionLoadoutScreen,
} from "../../src/ui/screens/LabAndLoadoutScreens";
import { MainMenuScreen } from "../../src/ui/screens/MainMenuScreen";
import { createTestServices } from "../support/fixtures";

describe("Screen smoke & navigation regression (offline engine)", () => {
  it("main menu reports the engine offline and disables journeys", async () => {
    const services = createTestServices();
    render(<MainMenuScreen services={services} onNavigate={() => {}} />);
    await waitFor(() => expect(screen.getByText("ENGINE OFFLINE")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: "NEW JOURNEY" })).toBeDisabled();
  });

  it.each([
    ["CARD & HAND LAB", "card_hand_lab"],
    ["INSCRIPTION LOADOUT LAB", "loadout_lab"],
    ["RUN HISTORY", "history"],
    ["RULES SANDBOX", "sandbox"],
    ["CONTENT CODEX", "codex"],
    ["SETTINGS", "settings"],
  ])("main menu '%s' navigates to %s", async (label, target) => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<MainMenuScreen services={services} onNavigate={onNavigate} />);
    await userEvent.click(screen.getByRole("button", { name: new RegExp(label.replace("&", "\\&")) }));
    expect(onNavigate).toHaveBeenCalledWith(target);
  });

  it("card & hand lab mounts offline and can return to the menu", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<CardHandLabScreen services={services} onNavigate={onNavigate} />);
    const back = screen.getAllByRole("button", { name: /MAIN MENU/ })[0];
    await userEvent.click(back);
    expect(onNavigate).toHaveBeenCalledWith("menu");
  });

  it("inscription loadout lab mounts with fixture slots", () => {
    const services = createTestServices();
    render(<InscriptionLoadoutScreen services={services} onNavigate={() => {}} />);
    expect(screen.getByText("INSCRIPTION & EQUIPMENT SLOTS")).toBeInTheDocument();
  });

  it("settings screen mounts", () => {
    const services = createTestServices();
    const { container } = render(<SettingsScreen services={services} onNavigate={() => {}} />);
    expect(container.textContent?.length ?? 0).toBeGreaterThan(0);
  });
});
