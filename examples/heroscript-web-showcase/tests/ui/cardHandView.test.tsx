import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { I18n } from "../../src/presentation/i18n";
import { CardHandView } from "../../src/ui/components/CardHandView";
import { cardModel } from "../support/fixtures";

const i18n = new I18n("en");

function hand(count: number) {
  return Array.from({ length: count }, (_, i) =>
    cardModel({ cardInstanceId: `c${i}`, name: `Card ${i}` })
  );
}

describe("CardHandView regression", () => {
  it("renders an empty-hand message", () => {
    render(<CardHandView cards={[]} translator={i18n} />);
    expect(screen.getByText("No cards in hand.")).toBeInTheDocument();
  });

  it("renders every card and the hand count", () => {
    render(<CardHandView cards={hand(5)} translator={i18n} />);
    for (let i = 0; i < 5; i++) {
      expect(screen.getByText(`Card ${i}`)).toBeInTheDocument();
    }
    expect(screen.getByText("(5)")).toBeInTheDocument();
  });

  it("reports the selected instance id", async () => {
    const onSelect = vi.fn();
    render(<CardHandView cards={hand(3)} translator={i18n} onSelectCard={onSelect} />);
    await userEvent.click(screen.getByText("Card 2"));
    expect(onSelect).toHaveBeenCalledWith("c2");
  });

  it("reports hover enter and leave", async () => {
    const onHover = vi.fn();
    render(<CardHandView cards={hand(2)} translator={i18n} onHoverCard={onHover} />);
    await userEvent.hover(screen.getByText("Card 1"));
    expect(onHover).toHaveBeenCalledWith("c1");
    await userEvent.unhover(screen.getByText("Card 1"));
    expect(onHover).toHaveBeenLastCalledWith("");
  });

  it("opens and closes the hand overview", async () => {
    render(<CardHandView cards={hand(3)} translator={i18n} />);
    await userEvent.click(screen.getByRole("button", { name: "Hand overview" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Card 1")).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("selecting from the overview closes it and reports the card", async () => {
    const onSelect = vi.fn();
    render(<CardHandView cards={hand(3)} translator={i18n} onSelectCard={onSelect} />);
    await userEvent.click(screen.getByRole("button", { name: "Hand overview" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByText("Card 0"));
    expect(onSelect).toHaveBeenCalledWith("c0");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("inspect button targets the selected card", async () => {
    const onInspect = vi.fn();
    render(
      <CardHandView cards={hand(3)} selectedId="c1" translator={i18n} onInspectCard={onInspect} />
    );
    await userEvent.click(screen.getByRole("button", { name: "Inspect card" }));
    expect(onInspect).toHaveBeenCalledWith("c1");
  });
});
