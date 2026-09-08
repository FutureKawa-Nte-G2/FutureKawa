import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { apiRequest, registerAuthRefreshHandler, ApiError } from "./client";

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status });
}

describe("apiRequest 401/refresh retry", () => {
  beforeEach(() => {
    registerAuthRefreshHandler(null);
  });
  afterEach(() => {
    vi.restoreAllMocks();
    registerAuthRefreshHandler(null);
  });

  it("retries once with the refreshed token after a 401, and succeeds", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(401, null))
      .mockResolvedValueOnce(jsonResponse(200, { success: true, data: { ok: true }, message: null, errors: null }));

    registerAuthRefreshHandler(async () => "new-token");

    const result = await apiRequest<{ ok: boolean }>("/api/whatever", { accessToken: "expired-token" });

    expect(result).toEqual({ ok: true });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    const secondCallHeaders = fetchMock.mock.calls[1][1]?.headers as Record<string, string>;
    expect(secondCallHeaders["Authorization"]).toBe("Bearer new-token");
  });

  it("throws the original 401 when the refresh handler itself fails", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(401, null));
    registerAuthRefreshHandler(async () => null);

    await expect(apiRequest("/api/whatever", { accessToken: "expired-token" })).rejects.toThrow(ApiError);
  });

  it("does not retry when skipAuthRetry is set (prevents refresh() from recursing on itself)", async () => {
    const fetchMock = vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(401, null));
    const handler = vi.fn();
    registerAuthRefreshHandler(handler);

    await expect(apiRequest("/api/auth/refresh", { skipAuthRetry: true })).rejects.toThrow(ApiError);
    expect(handler).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("does not retry when no handler is registered", async () => {
    const fetchMock = vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(401, null));

    await expect(apiRequest("/api/whatever", { accessToken: "expired-token" })).rejects.toThrow(ApiError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});