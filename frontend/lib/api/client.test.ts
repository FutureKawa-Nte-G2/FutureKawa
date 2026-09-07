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
describe("apiRequest — concurrence, corps non-JSON, timeout", () => {
  beforeEach(() => {
    registerAuthRefreshHandler(null);
  });
  afterEach(() => {
    vi.restoreAllMocks();
    registerAuthRefreshHandler(null);
  });

  it("shares one refresh between concurrent 401s instead of racing the rotation", async () => {
    // Head office revokes the old refresh token before issuing a new one, so a
    // second concurrent refresh would present a token already revoked, fail,
    // and sign the user out. /fifo fires three authenticated calls on mount.
    vi.spyOn(global, "fetch").mockImplementation(async (_url, init) => {
      const headers = (init?.headers ?? {}) as Record<string, string>;
      return headers["Authorization"] === "Bearer fresh"
        ? jsonResponse(200, { success: true, data: { ok: true }, message: null, errors: null })
        : jsonResponse(401, null);
    });

    let refreshCalls = 0;
    registerAuthRefreshHandler(async () => {
      refreshCalls += 1;
      await new Promise((resolve) => setTimeout(resolve, 10));
      return "fresh";
    });

    const results = await Promise.all([
      apiRequest("/api/batches", { accessToken: "expired" }),
      apiRequest("/api/countries", { accessToken: "expired" }),
      apiRequest("/api/alerts", { accessToken: "expired" }),
    ]);

    expect(refreshCalls).toBe(1);
    expect(results).toEqual([{ ok: true }, { ok: true }, { ok: true }]);
  });

  it("allows a later refresh once the shared one has settled", async () => {
    // mockImplementation, not mockResolvedValue: a Response body can only be
    // read once, and both calls would otherwise share the same object.
    vi.spyOn(global, "fetch").mockImplementation(async () => jsonResponse(401, null));
    let refreshCalls = 0;
    registerAuthRefreshHandler(async () => {
      refreshCalls += 1;
      return null;
    });

    await expect(apiRequest("/api/batches", { accessToken: "expired" })).rejects.toBeInstanceOf(ApiError);
    await expect(apiRequest("/api/batches", { accessToken: "expired" })).rejects.toBeInstanceOf(ApiError);

    // Sequential calls each get their own attempt; only concurrent ones share.
    expect(refreshCalls).toBe(2);
  });

  it("keeps the HTTP status when the error body is plain text, not our envelope", async () => {
    // Program.cs answers exactly this on POST /api/alerts without the API key.
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response("Unauthorized: invalid or missing X-Api-Key.", { status: 401 })
    );

    await expect(apiRequest("/api/alerts", { skipAuthRetry: true })).rejects.toMatchObject({
      name: "ApiError",
      status: 401,
    });
  });

  it("keeps the HTTP status when a proxy answers HTML", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response("<html>Bad Gateway</html>", { status: 502 })
    );

    await expect(apiRequest("/api/batches")).rejects.toMatchObject({
      name: "ApiError",
      status: 502,
    });
  });

  it("gives up on a backend that accepts the connection and never answers", async () => {
    // Without a deadline the page stays on "Chargement..." forever.
    const timeout = new DOMException("The operation timed out.", "TimeoutError");
    vi.spyOn(global, "fetch").mockRejectedValueOnce(timeout);

    await expect(apiRequest("/api/batches")).rejects.toBe(timeout);
  });

  it("arms the timeout signal on every request", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, { success: true, data: null, message: null, errors: null }));

    await apiRequest("/api/batches");

    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal);
  });
});
