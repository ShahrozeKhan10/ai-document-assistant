import { useEffect, useRef, useState } from "react";
import { streamChat } from "../api";
import type { ChatMessage, DocumentSummary } from "../types";
import MessageView from "./MessageView";

interface Props {
  selectedDocuments: DocumentSummary[];
  readyCount: number;
}

const newId = () => Math.random().toString(36).slice(2);

export default function ChatPanel({ selectedDocuments, readyCount }: Props) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [streaming, setStreaming] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  // Keep the newest text in view while the answer streams in.
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [messages]);

  // Updates one message by id (React state must be replaced, not mutated).
  const patch = (id: string, change: (m: ChatMessage) => Partial<ChatMessage>) =>
    setMessages((all) => all.map((m) => (m.id === id ? { ...m, ...change(m) } : m)));

  async function send() {
    const question = input.trim();
    if (!question || streaming) return;

    const answerId = newId();
    setMessages((all) => [
      ...all,
      { id: newId(), role: "user", text: question },
      { id: answerId, role: "assistant", text: "", streaming: true },
    ]);
    setInput("");
    setStreaming(true);

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      await streamChat(
        question,
        selectedDocuments.map((d) => d.id),
        {
          onDelta: (text) => patch(answerId, (m) => ({ text: m.text + text })),
          onSources: (sources) => patch(answerId, () => ({ sources })),
          onError: (error) => patch(answerId, () => ({ error })),
        },
        controller.signal,
      );
    } catch {
      if (!controller.signal.aborted) {
        patch(answerId, () => ({
          error: "Could not reach the API. Is it running? (run.bat)",
        }));
      }
    } finally {
      patch(answerId, (m) => ({
        streaming: false,
        text: m.text || (controller.signal.aborted ? "_Stopped._" : m.text),
      }));
      setStreaming(false);
      abortRef.current = null;
    }
  }

  const scopeLabel =
    selectedDocuments.length === 0
      ? `Searching all ${readyCount} ready document${readyCount === 1 ? "" : "s"}`
      : `Searching ${selectedDocuments.map((d) => d.fileName).join(", ")}`;

  return (
    <main className="chat-panel">
      <header className="chat-header">
        <span className="scope" title={scopeLabel}>{scopeLabel}</span>
        {messages.length > 0 && (
          <button className="link-btn" disabled={streaming} onClick={() => setMessages([])}>
            New chat
          </button>
        )}
      </header>

      <div className="messages">
        {messages.length === 0 ? (
          <div className="welcome">
            <h2>Ask anything about your documents</h2>
            <p className="muted">
              Answers come only from your PDFs, with citations like [1] that show the file and page.
            </p>
            <div className="examples">
              {["Summarize this document", "What are the key dates mentioned?", "List the main skills"].map(
                (q) => (
                  <button key={q} className="example" onClick={() => setInput(q)}>
                    {q}
                  </button>
                ),
              )}
            </div>
          </div>
        ) : (
          messages.map((m) => <MessageView key={m.id} message={m} />)
        )}
        <div ref={bottomRef} />
      </div>

      <form
        className="composer"
        onSubmit={(e) => {
          e.preventDefault();
          void send();
        }}
      >
        <textarea
          value={input}
          placeholder={readyCount === 0 ? "Upload a PDF first…" : "Ask a question… (Enter to send, Shift+Enter for a new line)"}
          rows={2}
          disabled={readyCount === 0}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              void send();
            }
          }}
        />
        {streaming ? (
          <button type="button" className="send-btn stop" onClick={() => abortRef.current?.abort()}>
            Stop
          </button>
        ) : (
          <button type="submit" className="send-btn" disabled={!input.trim() || readyCount === 0}>
            Send
          </button>
        )}
      </form>
    </main>
  );
}
