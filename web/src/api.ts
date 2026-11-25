import type { AuthResponse, DocumentDetail, DocumentSummary, Source } from "./types";

// ---------- Auth token ----------
// The JWT is kept in memory and in localStorage (so a page refresh keeps you logged in).
// Every API call sends it as "Authorization: Bearer <token>".

const TOKEN_KEY = "docassistant.auth";
let auth: AuthResponse | null = loadAuth();
let onUnauthorized: () => void = () => {};

function loadAuth(): AuthResponse | null {
  try {
    const raw = localStorage.getItem(TOKEN_KEY);
    if (!raw) return null;
    const saved = JSON.parse(raw) as AuthResponse;
    return new Date(saved.expiresAtUtc).getTime() > Date.now() ? saved : null;
  } catch {
    return null;
  }
}

export function getAuth(): AuthResponse | null {
  return auth;
}

export function setAuth(value: AuthResponse | null): void {
  auth = value;
  try {
    if (value) localStorage.setItem(TOKEN_KEY, JSON.stringify(value));
    else localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Storage can be unavailable (private mode); the in-memory token still works.
  }
}

/** Called when the API says the token is missing or expired (HTTP 401). */
export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler;
}

/** fetch() that adds the bearer token and logs out on 401. */
async function authFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (auth) headers.set("Authorization", `Bearer ${auth.token}`);
  const res = await fetch(url, { ...init, headers });
  if (res.status === 401) {
    setAuth(null);
    onUnauthorized();
    throw new Error("Your session has expired. Please log in again.");
  }
  return res;
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  return authRequest("/api/auth/login", email, password);
}

export async function register(email: string, password: string): Promise<AuthResponse> {
  return authRequest("/api/auth/register", email, password);
}

async function authRequest(url: string, email: string, password: string): Promise<AuthResponse> {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

/** Turns an error response (ProblemDetails or plain text) into a readable message. */
async function readError(res: Response): Promise<string> {
  const body = await res.text();
  try {
    const problem = JSON.parse(body) as { detail?: string; title?: string };
    return problem.detail ?? problem.title ?? `Request failed (${res.status})`;
  } catch {
    return body || `Request failed (${res.status})`;
  }
}

export async function listDocuments(): Promise<DocumentSummary[]> {
  const res = await authFetch("/api/documents");
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export async function uploadDocument(file: File): Promise<DocumentDetail> {
  const form = new FormData();
  form.append("file", file);
  const res = await authFetch("/api/documents", { method: "POST", body: form });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export async function reindexDocument(id: string): Promise<DocumentDetail> {
  const res = await authFetch(`/api/documents/${id}/reindex`, { method: "POST" });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export async function deleteDocument(id: string): Promise<void> {
  const res = await authFetch(`/api/documents/${id}`, { method: "DELETE" });
  if (!res.ok) throw new Error(await readError(res));
}

export interface StreamHandlers {
  onDelta: (text: string) => void;
  onSources: (sources: Source[]) => void;
  onError: (message: string) => void;
}

/**
 * Calls POST /api/chat/stream and parses the Server-Sent Events by hand.
 * (EventSource only supports GET, so we read the fetch body stream instead.)
 */
export async function streamChat(
  question: string,
  documentIds: string[],
  handlers: StreamHandlers,
  signal: AbortSignal,
): Promise<void> {
  const res = await authFetch("/api/chat/stream", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question, documentIds }),
    signal,
  });

  if (!res.ok || !res.body) {
    handlers.onError(await readError(res));
    return;
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    // Events are separated by a blank line.
    let boundary = buffer.indexOf("\n\n");
    while (boundary !== -1) {
      const rawEvent = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      handleEvent(rawEvent, handlers);
      boundary = buffer.indexOf("\n\n");
    }
  }
}

function handleEvent(rawEvent: string, handlers: StreamHandlers): void {
  let type = "message";
  const dataLines: string[] = [];

  for (const line of rawEvent.split("\n")) {
    if (line.startsWith("event:")) type = line.slice(6).trim();
    else if (line.startsWith("data:")) dataLines.push(line.slice(5).trim());
  }
  if (dataLines.length === 0) return;

  const data = JSON.parse(dataLines.join("\n")) as {
    text?: string;
    sources?: Source[];
    message?: string;
  };

  switch (type) {
    case "delta":
      if (data.text) handlers.onDelta(data.text);
      break;
    case "sources":
      handlers.onSources(data.sources ?? []);
      break;
    case "error":
      handlers.onError(data.message ?? "Something went wrong.");
      break;
  }
}
