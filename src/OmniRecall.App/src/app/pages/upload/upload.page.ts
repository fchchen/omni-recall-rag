import { Component, DestroyRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { OmniRecallApiService } from '../../core/omni-recall-api.service';
import { UploadDocumentResponse } from '../../models/api.models';

@Component({
  selector: 'app-upload-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './upload.page.html',
  styleUrl: './upload.page.scss',
})
export class UploadPageComponent {
  private readonly destroyRef = inject(DestroyRef);

  selectedFile: File | null = null;
  sourceType = 'file';
  isSubmitting = false;
  error = '';
  result: UploadDocumentResponse | null = null;

  constructor(private readonly api: OmniRecallApiService) {}

  onFilePicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.result = null;
    this.error = '';
  }

  upload(): void {
    if (!this.selectedFile) {
      this.error = 'Pick a file first.';
      return;
    }

    this.isSubmitting = true;
    this.error = '';
    this.result = null;

    this.api.uploadDocument(this.selectedFile, this.sourceType).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (response) => {
        this.result = response;
        this.isSubmitting = false;
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Upload failed.';
        this.error = String(detail);
        this.isSubmitting = false;
      },
    });
  }
}
