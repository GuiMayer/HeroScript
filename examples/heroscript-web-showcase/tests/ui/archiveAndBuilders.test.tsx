import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import {
  CodexScreen,
  RunHistoryScreen,
  RunReplayScreen,
  SandboxScreen,
  SettingsScreen,
  TimelineScreen,
} from "../../src/ui/screens/ArchiveAndSettingsScreens";
import { DeckCompartmentScreen } from "../../src/ui/screens/DeckCompartmentScreen";
import { createTestServices } from "../support/fixtures";

describe("Archive, builders, codex & settings regression suite", () => {
  it("DeckCompartmentScreen renders header and handles menu navigation", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<DeckCompartmentScreen services={services} onNavigate={onNavigate} />);

    expect(screen.getByRole("heading", { name: "DECK COMPARTMENTS" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "VALIDATE ALLOCATION" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "START WITH COMPARTMENTS" })).toBeDisabled();

    const menuBtn = screen.getByRole("button", { name: "MAIN MENU" });
    await userEvent.click(menuBtn);
    expect(onNavigate).toHaveBeenCalledWith("menu");
  });

  it("TimelineScreen renders journal, inspects command sequences, and navigates back", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<TimelineScreen services={services} onNavigate={onNavigate} />);

    expect(screen.getByRole("heading", { name: "TIMELINE & BRANCHES" })).toBeInTheDocument();
    expect(screen.getByText("CREATE TIMELINE BRANCH")).toBeInTheDocument();

    const backBtn = screen.getByRole("button", { name: "BACK" });
    await userEvent.click(backBtn);
    expect(onNavigate).toHaveBeenCalledWith("journey");
  });

  it("RunHistoryScreen renders empty state offline and navigates to main menu", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(
      <RunHistoryScreen
        services={services}
        onSelectReplay={() => {}}
        onNavigate={onNavigate}
      />
    );

    expect(
      screen.getByRole("heading", { name: "EXPEDITION ARCHIVE & RUN HISTORY" })
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(
        screen.getByText("No recorded runs found on the connected HeroScript server.")
      ).toBeInTheDocument();
    });

    const menuBtn = screen.getByRole("button", { name: "MAIN MENU" });
    await userEvent.click(menuBtn);
    expect(onNavigate).toHaveBeenCalledWith("menu");
  });

  it("RunReplayScreen initializes playback controller and back button", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(
      <RunReplayScreen
        services={services}
        runId="run_demo_101"
        onNavigate={onNavigate}
      />
    );

    expect(
      screen.getByRole("heading", { name: /DETERMINISTIC REPLAY/ })
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Prev/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Next/ })).toBeInTheDocument();

    const backBtn = screen.getByRole("button", { name: "BACK" });
    await userEvent.click(backBtn);
    expect(onNavigate).toHaveBeenCalledWith("history");
  });

  it("SandboxScreen loads synergy scenarios and seed controls", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<SandboxScreen services={services} onNavigate={onNavigate} />);

    expect(
      screen.getByRole("heading", { name: "RULES LAB & SYNERGY SANDBOX" })
    ).toBeInTheDocument();
    expect(screen.getByText("Run Launch Payload (JSON)")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "LAUNCH SANDBOX RUN" })
    ).toBeDisabled();

    const menuBtn = screen.getByRole("button", { name: "MAIN MENU" });
    await userEvent.click(menuBtn);
    expect(onNavigate).toHaveBeenCalledWith("menu");
  });

  it("CodexScreen lists lore and rules categories", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<CodexScreen services={services} onNavigate={onNavigate} />);

    expect(
      screen.getByRole("heading", { name: "PUBLISHED CONTENT CODEX" })
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "cards" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "entities" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "resources" })).toBeInTheDocument();

    const menuBtn = screen.getByRole("button", { name: "MAIN MENU" });
    await userEvent.click(menuBtn);
    expect(onNavigate).toHaveBeenCalledWith("menu");
  });

  it("SettingsScreen switches category tabs and manages preferences", async () => {
    const services = createTestServices();
    const onNavigate = vi.fn();
    render(<SettingsScreen services={services} onNavigate={onNavigate} />);

    expect(
      screen.getByRole("heading", { name: "PREFERENCES & SYSTEM CONFIGURATION" })
    ).toBeInTheDocument();

    // Verify default tab has Language select
    expect(screen.getByText("LANGUAGE / LOCALE")).toBeInTheDocument();

    // Switch to Audio tab
    await userEvent.click(screen.getByRole("button", { name: /AUDIO/ }));
    expect(screen.getByText("Master Volume")).toBeInTheDocument();

    // Switch to Display tab
    await userEvent.click(screen.getByRole("button", { name: /DISPLAY/ }));
    expect(screen.getByText("Text Scale")).toBeInTheDocument();

    // Switch to Accessibility tab
    await userEvent.click(screen.getByRole("button", { name: /ACCESSIBILITY/ }));
    expect(screen.getByText("Reduced Motion")).toBeInTheDocument();

    // Switch to Controls tab
    await userEvent.click(screen.getByRole("button", { name: /CONTROLS/ }));
    expect(screen.getByText("Reset Default Bindings")).toBeInTheDocument();

    // Switch to Connection tab
    await userEvent.click(screen.getByRole("button", { name: /CONNECTION/ }));
    expect(screen.getByText("HEROSCRIPT REST ENDPOINT")).toBeInTheDocument();

    const menuBtn = screen.getByRole("button", { name: "MAIN MENU" });
    await userEvent.click(menuBtn);
    expect(onNavigate).toHaveBeenCalledWith("menu");
  });
});
