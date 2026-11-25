import { useCallback, useEffect, useState } from "react";
import { getAuth, listDocuments, setAuth, setUnauthorizedHandler } from "./api";
import type { AuthResponse, DocumentSummary } from "./types";
import DocumentPanel from "./components/DocumentPanel";
import ChatPanel from "./components/ChatPanel";
import LoginPage from "./components/LoginPage";

export default function App() {
  const [auth, setAuthState] = useState<AuthResponse | null>(getAuth());
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  const logout = useCallback(() => {
    setAuth(null);
    setAuthState(null);
    setDocuments([]);
    setSelectedIds([]);
  }, []);

  // If any request comes back 401 (expired token), go back to the login screen.
  useEffect(() => {
    setUnauthorizedHandler(logout);
  }, [logout]);

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
    if (auth) void refresh();
  }, [auth, refresh]);

  if (!auth) {
    return (
      <LoginPage
        onLoggedIn={(value) => {
          setAuth(value);
          setAuthState(value);
        }}
      />
    );
  }

  const toggleSelected = (id: string) =>
    setSelectedIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));

  const readyCount = documents.filter((d) => d.status === "Ready").length;

  return (
    <div className="app">
      <DocumentPanel
        documents={documents}
        selectedIds={selectedIds}
        loadError={loadError}
        userEmail={auth.email}
        onToggle={toggleSelected}
        onChanged={refresh}
        onLogout={logout}
      />
      <ChatPanel
        // A new key resets the chat when a different user logs in.
        key={auth.email}
        selectedDocuments={documents.filter((d) => selectedIds.includes(d.id))}
        readyCount={readyCount}
      />
    </div>
  );
}
