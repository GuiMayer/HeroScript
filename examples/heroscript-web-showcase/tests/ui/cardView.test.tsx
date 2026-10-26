import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CardView } from "../../src/ui/components/CardView";
import { cardModel } from "../support/fixtures";

describe("CardView regression", () => {
  it("renders the engine-published name, cost and effect text verbatim", () => {
    render(<CardView model={cardModel()} />);
    expect(screen.getByText("Ember Strike")).toBeInTheDocument();
    expect(screen.getByText("ATTACK")).toBeInTheDocument();
    // Effect segments are concatenated without client-side math.
    expect(screen.getByText(/Deal/)).toBeInTheDocument();
    expect(screen.getByText("6")).toBeInTheDocument();
  });

  it("renders every published cost token amount", () => {
    render(
      <CardView
        model={cardModel({
          costs: [
            { amount: 2, symbol: "⚡", name: "Energy" },
            { amount: 3, symbol: "✦", name: "Mana" },
          ],
        })}
      />
    );
    expect(screen.getByTitle("Energy")).toHaveTextContent("2");
    expect(screen.getByTitle("Mana")).toHaveTextContent("3");
  });

  it("shows an unknown-cost marker when costs are not published", () => {
    render(<CardView model={cardModel({ costsKnown: false, costUnknownLabel: "Cost hidden" })} />);
    expect(screen.getByTitle("Cost hidden")).toHaveTextContent("?");
  });

  it("falls back to the summary when no effect rows exist", () => {
    render(<CardView model={cardModel({ effectRows: [], summary: "Gain 5 guard." })} />);
    expect(screen.getByText("Gain 5 guard.")).toBeInTheDocument();
  });

  it("invokes onClick when the card is activated", async () => {
    const onClick = vi.fn();
    render(<CardView model={cardModel()} onClick={onClick} />);
    await userEvent.click(screen.getByText("Ember Strike"));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("inspect control calls onInspect without triggering onClick", async () => {
    const onClick = vi.fn();
    const onInspect = vi.fn();
    render(<CardView model={cardModel()} onClick={onClick} onInspect={onInspect} />);
    await userEvent.click(screen.getByTitle("Inspect Card Details"));
    expect(onInspect).toHaveBeenCalledTimes(1);
    expect(onClick).not.toHaveBeenCalled();
  });

  it("context menu opens inspection", () => {
    const onInspect = vi.fn();
    render(<CardView model={cardModel()} onInspect={onInspect} />);
    fireEvent.contextMenu(screen.getByText("Ember Strike"));
    expect(onInspect).toHaveBeenCalledTimes(1);
  });

  it("marks upgraded cards", () => {
    render(<CardView model={cardModel({ upgraded: true })} />);
    expect(screen.getByTitle("Upgraded")).toBeInTheDocument();
  });

  it("applies the burn animation class while burning", () => {
    const { container } = render(<CardView model={cardModel()} burning />);
    expect(container.querySelector(".card-burning")).not.toBeNull();
  });
});
