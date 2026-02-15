import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ChatResponse,
  DocumentChunkPreview,
  DocumentListItem,
  RecallSearchResponse,
  ReindexDocumentResponse,
  UploadDocumentResponse,
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class OmniRecallApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = this.resolveApiBase(environment.apiBaseUrl);

  uploadDocument(file: File, sourceType = 'file'): Observable<UploadDocumentResponse> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('sourceType', sourceType);
    return this.http.post<UploadDocumentResponse>(`${this.apiBase}/documents/upload`, formData);
  }

  searchRecall(query: string, topK = 5): Observable<RecallSearchResponse> {
    return this.http.post<RecallSearchResponse>(`${this.apiBase}/recall/search`, { query, topK });
  }

  chat(prompt: string, topK = 5): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(`${this.apiBase}/chat`, { prompt, topK });
  }

  listDocuments(maxCount = 100): Observable<DocumentListItem[]> {
    return this.http.get<DocumentListItem[]>(`${this.apiBase}/documents?maxCount=${maxCount}`);
  }

  getDocumentChunks(documentId: string, maxCount = 200): Observable<DocumentChunkPreview[]> {
    return this.http.get<DocumentChunkPreview[]>(
      `${this.apiBase}/documents/${encodeURIComponent(documentId)}/chunks?maxCount=${maxCount}`
    );
  }

  deleteDocument(documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiBase}/documents/${encodeURIComponent(documentId)}`);
  }

  reindexDocument(documentId: string): Observable<ReindexDocumentResponse> {
    return this.http.post<ReindexDocumentResponse>(
      `${this.apiBase}/documents/${encodeURIComponent(documentId)}/reindex`,
      {}
    );
  }

  private resolveApiBase(configured: string): string {
    const normalized = (configured ?? '').trim();
    if (!normalized) {
      return '/api';
    }

    return normalized.replace(/\/+$/, '');
  }
}
