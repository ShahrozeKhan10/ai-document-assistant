import { useCallback, useEffect, useState } from "react";
import { listDocuments } from "./api";
import type { DocumentSummary } from "./types";
import DocumentPanel from "./components/DocumentPanel";
import ChatPanel from "./components/ChatPanel";

export default function App() {
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const docs = await listDocuments();
      setDocuments(docs);
      // Drop selections for documents that no longer exist.
      setSelectedIds((ids) => ids.filter((id) => docs.some((d) => d.id === id)));
      setLoadError(null);
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : "Could not load documents.");
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const toggleSelected = (id: string) =>
    setSelectedIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));

  const readyCount = documents.filter((d) => d.status === "Ready").length;

  return (
    <div className="app">
      <DocumentPanel
        documents={documents}
        selectedIds={selectedIds}
        loadError={loadError}
        onToggle={toggleSelected}
        onChanged={refresh}
      />
      <ChatPanel
        selectedDocuments={documents.filter((d) => selectedIds.includes(d.id))}
        readyCount={readyCount}
      />
    </div>
  );
}
