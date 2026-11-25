import { useRef, useState } from "react";
import { deleteDocument, reindexDocument, uploadDocument } from "../api";
import type { DocumentSummary } from "../types";

interface Props {
  documents: DocumentSummary[];
  selectedIds: string[];
  loadError: string | null;
  onToggle: (id: string) => void;
  onChanged: () => Promise<void>;
}

function formatSize(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default function DocumentPanel({ documents, selectedIds, loadError, onToggle, onChanged }: Props) {
  const fileInput = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState<string | null>(null); // what is happening right now
  const [message, setMessage] = useState<{ kind: "ok" | "error"; text: string } | null>(null);

  async function run(label: string, action: () => Promise<string | void>) {
    setBusy(label);
    setMessage(null);
    try {
      const result = await action();
      if (result) setMessage({ kind: "ok", text: result });
    } catch (e) {
      setMessage({ kind: "error", text: e instanceof Error ? e.message : "Something went wrong." });
    } finally {
      setBusy(null);
      await onChanged();
    }
  }

  function handleFiles(files: FileList | null) {
    const file = files?.[0];
    if (!file) return;
    void run(`Uploading ${file.name}…`, async () => {
      const doc = await uploadDocument(file);
      if (doc.status === "Failed") throw new Error(doc.error ?? "Processing failed.");
      return `${doc.fileName}: ${doc.pageCount} pages, ${doc.chunkCount} chunks indexed.`;
    });
    if (fileInput.current) fileInput.current.value = "";
  }

  return (
    <aside className="doc-panel">
      <div className="brand">
        <span className="brand-mark">◆</span>
        <div>
          <h1>Document Assistant</h1>
          <p>Ask questions about your PDFs</p>
        </div>
      </div>

      <button
        className="upload-btn"
        disabled={busy !== null}
        onClick={() => fileInput.current?.click()}
      >
        {busy ?? "+ Upload PDF"}
      </button>
      <input
        ref={fileInput}
        type="file"
        accept="application/pdf,.pdf"
        hidden
        onChange={(e) => handleFiles(e.target.files)}
      />

      {message && <div className={`notice notice-${message.kind}`}>{message.text}</div>}
      {loadError && <div className="notice notice-error">{loadError}</div>}

      <div className="doc-list-header">
        <span>Documents</span>
        <span className="muted">{documents.length}</span>
      </div>

      {documents.length === 0 && !loadError && (
        <p className="muted empty">No documents yet. Upload a PDF to get started.</p>
      )}

      <ul className="doc-list">
        {documents.map((doc) => {
          const selected = selectedIds.includes(doc.id);
          const ready = doc.status === "Ready";
          return (
            <li key={doc.id} className={`doc ${selected ? "doc-selected" : ""}`}>
              <label className="doc-main">
                <input
                  type="checkbox"
                  checked={selected}
                  disabled={!ready}
                  onChange={() => onToggle(doc.id)}
                />
                <span className="doc-text">
                  <span className="doc-name" title={doc.fileName}>{doc.fileName}</span>
                  <span className="doc-meta">
                    {doc.pageCount} pages · {doc.chunkCount} chunks · {formatSize(doc.sizeBytes)}
                  </span>
                </span>
              </label>
              <div className="doc-actions">
                <span className={`badge badge-${doc.status.toLowerCase()}`}>{doc.status}</span>
                {!ready && (
                  <button
                    className="link-btn"
                    disabled={busy !== null}
                    onClick={() =>
                      void run(`Re-indexing ${doc.fileName}…`, async () => {
                        const d = await reindexDocument(doc.id);
                        if (d.status === "Failed") throw new Error(d.error ?? "Re-indexing failed.");
                        return `${d.fileName} re-indexed.`;
                      })
                    }
                  >
                    Retry
                  </button>
                )}
                <button
                  className="link-btn danger"
                  disabled={busy !== null}
                  onClick={() => {
                    if (!window.confirm(`Delete "${doc.fileName}"? This cannot be undone.`)) return;
                    void run(`Deleting ${doc.fileName}…`, async () => {
                      await deleteDocument(doc.id);
                    });
                  }}
                >
                  Delete
                </button>
              </div>
            </li>
          );
        })}
      </ul>

      <p className="muted hint">
        Tick documents to search only inside them. Nothing ticked = search all documents.
      </p>
    </aside>
  );
}
