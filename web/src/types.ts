// Shapes returned by the ASP.NET Core API (see DTOs/*.cs).

export type DocumentStatus = "Processing" | "Ready" | "Failed";

export interface DocumentSummary {
  id: string;
  fileName: string;
  sizeBytes: number;
  pageCount: number;
  chunkCount: number;
  status: DocumentStatus;
  uploadedAtUtc: string;
}

export interface DocumentDetail extends DocumentSummary {
  error: string | null;
}

export interface Source {
  number: number;
  documentId: string;
  fileName: string;
  pageNumber: number;
  score: number;
  snippet: string;
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  text: string;
  sources?: Source[];
  error?: string;
  streaming?: boolean;
}

export interface AuthResponse {
  token: string;
  email: string;
  expiresAtUtc: string;
}
