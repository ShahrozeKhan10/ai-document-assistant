import type { DocumentDetail, DocumentSummary, Source } from "./types";

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
  const res = await fetch("/api/documents");
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export async function uploadDocument(file: File): Promise<DocumentDetail> {
  const form = new FormData();
  form.append("file", file);
  const res = await fetch("/api/documents", { method: "POST", body: form });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export async function reindexDocument(id: string): Promise<DocumentDetail> {
  const res = await fetch(`/api/documents/${id}/reindex`, { method: "POST" });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export async function deleteDocument(id: string): Promise<void> {
  const res = await fetch(`/api/documents/${id}`, { method: "DELETE" });
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
  const res = await fetch("/api/chat/stream", {
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
