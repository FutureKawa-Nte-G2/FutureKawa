import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Pagination } from "./Pagination";

describe("Pagination", () => {
  it("renders every page number when the total fits without ellipsis", () => {
    render(<Pagination currentPage={1} totalPages={5} onPageChange={vi.fn()} />);

    ["1", "2", "3", "4", "5"].forEach((label) => {
      expect(screen.getByRole("button", { name: label })).toBeInTheDocument();
    });
    expect(screen.queryByText("…")).not.toBeInTheDocument();
  });

  it("shows a single trailing ellipsis when the current page is near the start", () => {
    render(<Pagination currentPage={1} totalPages={10} onPageChange={vi.fn()} />);

    ["1", "2", "3", "4", "5"].forEach((label) => {
      expect(screen.getByRole("button", { name: label })).toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "10" })).toBeInTheDocument();
    expect(screen.getAllByText("…")).toHaveLength(1);
  });

  it("shows ellipsis on both sides when the current page is in the middle", () => {
    render(<Pagination currentPage={6} totalPages={10} onPageChange={vi.fn()} />);

    expect(screen.getByRole("button", { name: "1" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "10" })).toBeInTheDocument();
    ["4", "5", "6", "7", "8"].forEach((label) => {
      expect(screen.getByRole("button", { name: label })).toBeInTheDocument();
    });
    expect(screen.getAllByText("…")).toHaveLength(2);
  });

  it("marks the current page with aria-current", () => {
    render(<Pagination currentPage={3} totalPages={10} onPageChange={vi.fn()} />);

    expect(screen.getByRole("button", { name: "3" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("button", { name: "4" })).not.toHaveAttribute("aria-current");
  });

  it("calls onPageChange with the clicked page number", async () => {
    const onPageChange = vi.fn();
    const user = userEvent.setup();
    render(<Pagination currentPage={1} totalPages={5} onPageChange={onPageChange} />);

    await user.click(screen.getByRole("button", { name: "3" }));

    expect(onPageChange).toHaveBeenCalledWith(3);
  });

  it("disables the previous arrow on the first page, and the next arrow on the last page", () => {
    const { rerender } = render(<Pagination currentPage={1} totalPages={5} onPageChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Page précédente" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Page suivante" })).not.toBeDisabled();

    rerender(<Pagination currentPage={5} totalPages={5} onPageChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Page précédente" })).not.toBeDisabled();
    expect(screen.getByRole("button", { name: "Page suivante" })).toBeDisabled();
  });

  it("navigates via the previous/next arrows", async () => {
    const onPageChange = vi.fn();
    const user = userEvent.setup();
    render(<Pagination currentPage={3} totalPages={5} onPageChange={onPageChange} />);

    await user.click(screen.getByRole("button", { name: "Page suivante" }));
    expect(onPageChange).toHaveBeenCalledWith(4);

    await user.click(screen.getByRole("button", { name: "Page précédente" }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("renders a single page 1 with both arrows disabled when totalPages is 1", () => {
    render(<Pagination currentPage={1} totalPages={1} onPageChange={vi.fn()} />);

    expect(screen.getByRole("button", { name: "1" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Page précédente" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Page suivante" })).toBeDisabled();
  });
});