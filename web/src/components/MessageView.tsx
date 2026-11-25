import { useState } from "react";
import ReactMarkdown from "react-markdown";
import type { ChatMessage } from "../types";

/** Turns "[2]" in the answer into a markdown link "[[2]](#src-2)" so we can render it as a citation chip. */
function linkCitations(text: string): string {
  return text.replace(/\[(\d+)\](?!\()/g, "[[$1]](#src-$1)");
}

export default function MessageView({ message }: { message: ChatMessage }) {
  const [active, setActive] = useState<number | null>(null);

  if (message.role === "user") {
    return (
      <div className="msg msg-user">
        <div className="bubble">{message.text}</div>
      </div>
    );
  }

  const sources = message.sources ?? [];

  return (
    <div className="msg msg-assistant">
      <div className="bubble">
        {message.text ? (
          <ReactMarkdown
            components={{
              a: ({ href, children }) => {
                if (href?.startsWith("#src-")) {
                  const n = Number(href.slice(5));
                  return (
                    <button
                      type="button"
                      className={`cite ${active === n ? "cite-active" : ""}`}
                      onClick={() => setActive(active === n ? null : n)}
                      title="Show source"
                    >
                      {n}
                    </button>
                  );
                }
                return (
                  <a href={href} target="_blank" rel="noreferrer">
                    {children}
                  </a>
                );
              },
            }}
          >
            {linkCitations(message.text)}
          </ReactMarkdown>
        ) : (
          message.streaming && !message.error && <span className="typing">Searching your documents…</span>
        )}
        {message.streaming && message.text && <span className="cursor" />}
        {message.error && <div className="notice notice-error">{message.error}</div>}
      </div>

      {sources.length > 0 && (
        <div className="sources">
          {sources.map((s) => (
            <button
              key={s.number}
              type="button"
              className={`source ${active === s.number ? "source-active" : ""}`}
              onClick={() => setActive(active === s.number ? null : s.number)}
            >
              <span className="source-head">
                <span className="cite">{s.number}</span>
                <span className="source-file">{s.fileName}</span>
                <span className="muted">p. {s.pageNumber}</span>
              </span>
              {active === s.number && <span className="source-snippet">{s.snippet}</span>}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
