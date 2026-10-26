import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { I18n } from "../../src/presentation/i18n";
import {
  ActorPanel,
  CardDetailsModal,
  CardZoneDialog,
  ResourceView,
} from "../../src/ui/components/CombatWidgets";
import { actorItem, cardModel } from "../support/fixtures";

const i18n = new I18n("en");

describe("Combat widgets regression", () => {
  it("ResourceView shows current and maximum published values", () => {
    render(
      <ResourceView
        pool={{ resourceId: "health", current: 7, minimum: 0, maximum: 30 }}
        translator={i18n}
      />
    );
    expect(screen.getByText("7 / 30")).toBeInTheDocument();
  });

  it("ResourceView omits the maximum for unbounded pools", () => {
    render(<ResourceView pool={{ resourceId: "gold", current: 99 }} translator={i18n} />);
    expect(screen.getByText("99")).toBeInTheDocument();
  });

  it("ActorPanel renders name, intent, resources and statuses", () => {
    render(<ActorPanel item={actorItem()} translator={i18n} />);
    expect(screen.getByText("Attack for 8")).toBeInTheDocument();
    expect(screen.getByText("12 / 20")).toBeInTheDocument();
    expect(screen.getByTitle("burn")).toHaveTextContent("×2");
  });

  it("ActorPanel only reports clicks when it is a valid target", async () => {
    const onSelect = vi.fn();
    const { rerender } = render(
      <ActorPanel item={actorItem()} translator={i18n} onSelectTarget={onSelect} />
    );
    await userEvent.click(screen.getByText("Attack for 8"));
    expect(onSelect).not.toHaveBeenCalled();

    rerender(
      <ActorPanel item={actorItem()} validTarget translator={i18n} onSelectTarget={onSelect} />
    );
    expect(screen.getByText("SELECT TARGET")).toBeInTheDocument();
    await userEvent.click(screen.getByText("Attack for 8"));
    expect(onSelect).toHaveBeenCalledWith("enemy_1");
  });

  it("ActorPanel shows floating combat feedback", () => {
    render(
      <ActorPanel
        item={actorItem()}
        floatingTexts={[{ id: "f1", text: "-6 Health", tone: "DAMAGE" }]}
        translator={i18n}
      />
    );
    expect(screen.getByText("-6 Health")).toBeInTheDocument();
  });

  it("CardDetailsModal renders nothing without a model", () => {
    const { container } = render(
      <CardDetailsModal model={null} translator={i18n} onClose={() => {}} />
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("CardDetailsModal shows inspection text and closes", async () => {
    const onClose = vi.fn();
    render(
      <CardDetailsModal
        model={cardModel()}
        inspectionText="Engine fact: 6 damage"
        translator={i18n}
        onClose={onClose}
      />
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Engine fact: 6 damage")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("CardZoneDialog distinguishes hidden and empty zones", () => {
    const { rerender } = render(
      <CardZoneDialog title="Draw" cards={[]} visible={false} translator={i18n} onClose={() => {}} />
    );
    expect(screen.getByText("Contents of this zone are hidden by game rules.")).toBeInTheDocument();
    rerender(<CardZoneDialog title="Draw" cards={[]} translator={i18n} onClose={() => {}} />);
    expect(screen.getByText("This zone is currently empty.")).toBeInTheDocument();
  });

  it("CardZoneDialog lists zone cards", () => {
    render(
      <CardZoneDialog
        title="Discard"
        cards={[cardModel({ name: "Spent Spark" })]}
        translator={i18n}
        onClose={() => {}}
      />
    );
    expect(screen.getByText("Spent Spark")).toBeInTheDocument();
  });
});
