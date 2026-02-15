export interface UploadDocumentResponse {
  documentId: string;
  fileName: string;
  sourceType: string;
  blobPath: string;
  chunkCount: number;
  contentHash: string;
  createdAtUtc: string;
}

export interface RecallCitation {
  documentId: string;
  fileName: string;
  chunkId: string;
  chunkIndex: number;
  snippet: string;
  score: number;
  createdAtUtc: string;
}

export interface RecallSearchResponse {
  query: string;
  citations: RecallCitation[];
}

export interface ChatResponse {
  answer: string;
  provider: string;
  model: string;
  citations: RecallCitation[];
}

export interface DocumentListItem {
  documentId: string;
  fileName: string;
  sourceType: string;
  chunkCount: number;
  createdAtUtc: string;
}

export interface DocumentChunkPreview {
  chunkId: string;
  chunkIndex: number;
  snippet: string;
  hasEmbedding: boolean;
  createdAtUtc: string;
}

export interface ReindexDocumentResponse {
  documentId: string;
  chunkCount: number;
  embeddedCount: number;
  rateLimitedCount: number;
  emptyCount: number;
  failedCount: number;
  reindexedAtUtc: string;
}
