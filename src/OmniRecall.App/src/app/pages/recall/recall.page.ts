import { Component, DestroyRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { OmniRecallApiService } from '../../core/omni-recall-api.service';
import { RecallCitation } from '../../models/api.models';

@Component({
  selector: 'app-recall-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './recall.page.html',
  styleUrl: './recall.page.scss',
})
export class RecallPageComponent {
  private readonly destroyRef = inject(DestroyRef);

  query = '';
  topK = 5;
  isLoading = false;
  error = '';
  citations: RecallCitation[] = [];

  constructor(private readonly api: OmniRecallApiService) {}

  search(): void {
    if (!this.query.trim()) {
      this.error = 'Enter a query first.';
      return;
    }

    this.isLoading = true;
    this.error = '';
    this.citations = [];

    this.api.searchRecall(this.query, this.topK).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (response) => {
        this.citations = response.citations;
        this.isLoading = false;
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Search failed.';
        this.error = String(detail);
        this.isLoading = false;
      },
    });
  }
}
