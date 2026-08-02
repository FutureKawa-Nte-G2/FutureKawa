import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PageSizeSelector } from "./PageSizeSelector";

describe("PageSizeSelector", () => {
  it("displays the three page size options", () => {
    render(<PageSizeSelector value={10} onChange={vi.fn()} />);

    expect(screen.getByRole("option", { name: "10" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "15" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "20" })).toBeInTheDocument();
  });

  it("reflects the current value", () => {
    render(<PageSizeSelector value={15} onChange={vi.fn()} />);
    expect(screen.getByRole("combobox")).toHaveValue("15");
  });

  it("calls onChange with a number, not a string, when a new size is selected", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<PageSizeSelector value={10} onChange={onChange} />);

    await user.selectOptions(screen.getByRole("combobox"), "20");

    expect(onChange).toHaveBeenCalledWith(20);
    expect(onChange).not.toHaveBeenCalledWith("20");
  });
});