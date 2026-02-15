import { Component, DestroyRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { OmniRecallApiService } from '../../core/omni-recall-api.service';
import { ChatResponse } from '../../models/api.models';

@Component({
  selector: 'app-chat-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './chat.page.html',
  styleUrl: './chat.page.scss',
})
export class ChatPageComponent {
  private readonly destroyRef = inject(DestroyRef);

  prompt = '';
  topK = 5;
  isLoading = false;
  error = '';
  result: ChatResponse | null = null;

  constructor(private readonly api: OmniRecallApiService) {}

  ask(): void {
    if (!this.prompt.trim()) {
      this.error = 'Enter a question.';
      return;
    }

    this.isLoading = true;
    this.error = '';
    this.result = null;

    this.api.chat(this.prompt, this.topK).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (response) => {
        this.result = response;
        this.isLoading = false;
      },
      error: (err) => {
        const detail = err?.error?.error ?? err?.message ?? 'Chat failed.';
        this.error = String(detail);
        this.isLoading = false;
      },
    });
  }

  get formattedAnswerHtml(): string {
    return this.formatAnswerHtml(this.result?.answer ?? '');
  }

  private formatAnswerHtml(answer: string): string {
    if (!answer.trim()) {
      return '';
    }

    const paragraphs = answer
      .trim()
      .split(/\n\s*\n+/)
      .map((p) => p.trim())
      .filter((p) => p.length > 0);

    return paragraphs
      .map((paragraph) => {
        const lines = paragraph
          .split('\n')
          .map((line) => line.trim())
          .filter((line) => line.length > 0);

        const isOrderedList = lines.length > 1 && lines.every((line) => /^\d+\.\s+/.test(line));
        if (isOrderedList) {
          const items = lines
            .map((line) => line.replace(/^\d+\.\s+/, ''))
            .map((line) => `<li>${this.formatInline(this.escapeHtml(line))}</li>`)
            .join('');
          return `<ol>${items}</ol>`;
        }

        const escaped = this.escapeHtml(paragraph).replace(/\n/g, '<br />');
        return `<p>${this.formatInline(escaped)}</p>`;
      })
      .join('');
  }

  private formatInline(text: string): string {
    return text.replace(/\[(\d+)\]/g, '<span class="citation-ref">[$1]</span>');
  }

  private escapeHtml(text: string): string {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
}
