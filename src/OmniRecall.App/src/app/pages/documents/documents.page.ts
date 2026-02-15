import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { OmniRecallApiService } from '../../core/omni-recall-api.service';
import {
  DocumentChunkPreview,
  DocumentListItem,
  ReindexDocumentResponse,
} from '../../models/api.models';

@Component({
  selector: 'app-documents-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './documents.page.html',
  styleUrl: './documents.page.scss',
})
export class DocumentsPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  documents: DocumentListItem[] = [];
  selectedDocument: DocumentListItem | null = null;
  chunks: DocumentChunkPreview[] = [];
  loading = false;
  error = '';
  actionMessage = '';
  lastReindex: ReindexDocumentResponse | null = null;

  constructor(private readonly api: OmniRecallApiService) {}

  ngOnInit(): void {
    this.refreshDocuments();
  }

  refreshDocuments(): void {
    this.loading = true;
    this.error = '';
    this.actionMessage = '';
    this.lastReindex = null;

    this.api.listDocuments().pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (docs) => {
        this.documents = docs;
        if (this.selectedDocument) {
          const stillExists = docs.find((d) => d.documentId === this.selectedDocument!.documentId);
          this.selectedDocument = stillExists ?? null;
          this.chunks = stillExists ? this.chunks : [];
        }
        this.loading = false;
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Failed to load documents.';
        this.error = String(detail);
        this.loading = false;
      },
    });
  }

  openDocument(doc: DocumentListItem): void {
    this.selectedDocument = doc;
    this.error = '';
    this.actionMessage = '';
    this.lastReindex = null;
    this.chunks = [];
    this.loading = true;

    this.api.getDocumentChunks(doc.documentId).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (chunks) => {
        this.chunks = chunks;
        this.loading = false;
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Failed to load chunks.';
        this.error = String(detail);
        this.loading = false;
      },
    });
  }

  deleteSelected(): void {
    if (!this.selectedDocument) return;

    const doc = this.selectedDocument;
    this.loading = true;
    this.error = '';
    this.actionMessage = '';
    this.lastReindex = null;

    this.api.deleteDocument(doc.documentId).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.selectedDocument = null;
        this.chunks = [];
        this.actionMessage = `Deleted ${doc.fileName}`;
        this.refreshDocuments();
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Delete failed.';
        this.error = String(detail);
        this.loading = false;
      },
    });
  }

  reindexSelected(): void {
    if (!this.selectedDocument) return;

    const doc = this.selectedDocument;
    this.loading = true;
    this.error = '';
    this.actionMessage = '';

    this.api.reindexDocument(doc.documentId).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (result) => {
        this.lastReindex = result;
        this.actionMessage = `Reindexed ${result.chunkCount} chunks for ${doc.fileName}`;
        this.openDocument(doc);
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Reindex failed.';
        this.error = String(detail);
        this.loading = false;
      },
    });
  }
}
