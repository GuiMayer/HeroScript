import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App } from "../../src/App";
import { createTestServices } from "../support/fixtures";

describe("App shell, layout & global toasts regression", () => {
  it("renders main menu by default and surfaces error/notice toasts reactively", async () => {
    const services = createTestServices();
    render(<App services={services} />);

    // Initial screen is main menu
    expect(screen.getByText("IVORY EDITION SHOWCASE")).toBeInTheDocument();

    // The initial loadCatalog attempt in test stub produces an initial connection alert
    const initialAlert = await screen.findByRole("alert");
    expect(initialAlert).toHaveTextContent("Connection to the engine failed");

    // Dismiss the initial error alert
    const dismissBtn = screen.getByRole("button", { name: "Dismiss" });
    await userEvent.click(dismissBtn);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    // Trigger a simulated error alert via session
    act(() => {
      services.session.errorReported.emit("Critical engine timeout detected");
    });

    const newAlert = await screen.findByRole("alert");
    expect(newAlert).toHaveTextContent("Critical engine timeout detected");

    // Dismiss the simulated error alert
    const dismissBtn2 = screen.getByRole("button", { name: "Dismiss" });
    await userEvent.click(dismissBtn2);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    // Trigger a notice status via session
    act(() => {
      services.session.noticeReported.emit("Expedition seed saved");
    });

    const notice = await screen.findByRole("status");
    expect(notice).toHaveTextContent("Expedition seed saved");
  });
});
