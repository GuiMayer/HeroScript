import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ActivityScreen } from "../../src/ui/screens/ActivityScreen";
import { CombatScreen } from "../../src/ui/screens/CombatScreen";
import { DialogueScreen } from "../../src/ui/screens/DialogueScreen";
import { MapScreen } from "../../src/ui/screens/MapScreen";
import { createTestServices } from "../support/fixtures";

describe("Gameplay screens regression (Combat, Map, Activity, Dialogue)", () => {
  it("CombatScreen renders party & opposition formations, tactical dock, and opens pile dialogs", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<CombatScreen services={services} onNavigate={onNavigate} />);

    expect(screen.getByText("EXPEDITION PARTY")).toBeInTheDocument();
    expect(screen.getByText("OPPOSITION")).toBeInTheDocument();
    expect(screen.getByText("TACTICAL PREVIEW")).toBeInTheDocument();

    // Open draw pile dialog
    await userEvent.click(screen.getByRole("button", { name: /drawPile/i }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("This zone is currently empty.")).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("button", { name: "Close" }));

    // Navigate to journey
    await userEvent.click(screen.getByRole("button", { name: "JOURNEY" }));
    expect(onNavigate).toHaveBeenCalledWith("journey");
  });

  it("MapScreen renders route atlas and navigates back to stage", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<MapScreen services={services} onNavigate={onNavigate} />);

    expect(screen.getByText("EXPEDITION ATLAS")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "RETURN TO STAGE" }));
    expect(onNavigate).toHaveBeenCalledWith("journey");
  });

  it("ActivityScreen renders expedition status, stage header, and navigation buttons", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<ActivityScreen services={services} onNavigate={onNavigate} />);

    expect(screen.getByText("EXPEDITION STATUS")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /OPEN MAP/i }));
    expect(onNavigate).toHaveBeenCalledWith("map");
  });

  it("DialogueScreen renders speaker stage and opens transcript history modal", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<DialogueScreen services={services} onNavigate={onNavigate} />);

    expect(screen.getByText("NARRATIVE ENCOUNTER")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /CONVERSATION HISTORY/i }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("No prior transcript lines recorded.")).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
