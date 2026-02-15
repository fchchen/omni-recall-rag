import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { OmniRecallApiService } from '../../core/omni-recall-api.service';
import { DocumentChunkPreview, DocumentListItem } from '../../models/api.models';

type EvalStatus = 'pending' | 'running' | 'pass' | 'partial' | 'fail' | 'error';

interface EvalCase {
  id: string;
  question: string;
  expectedFile: string;
  topK: number;
}

interface EvalResult {
  caseId: string;
  status: EvalStatus;
  recallHit: boolean;
  chatHit: boolean;
  expectedFile: string;
  recallTopFile: string;
  providerModel: string;
  detail: string;
  durationMs: number;
}

interface RecallProbeResult {
  caseId: string;
  expectedFile: string;
  question: string;
  topK: number;
  recallHit: boolean;
  topRecallFile: string;
  recallError: string;
}

interface BatchChatOutcome {
  available: boolean;
  chatHit: boolean;
  guardBlocked: boolean;
  providerModel: string;
  detail: string;
}

@Component({
  selector: 'app-eval-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './eval.page.html',
  styleUrl: './eval.page.scss',
})
export class EvalPageComponent implements OnInit {
  private readonly storageKey = 'omni-recall-rag.eval-cases.v1';
  private readonly interGroupDelayMs = 12_500;
  private readonly perRecallDelayMs = 900;
  private readonly providerUnavailableBackoffMs = 20_000;

  cases: EvalCase[] = [];
  results: EvalResult[] = [];
  isRunning = false;
  error = '';
  info = '';

  constructor(private readonly api: OmniRecallApiService) {}

  ngOnInit(): void {
    this.cases = this.loadCases();
    if (this.cases.length === 0) {
      void this.resetDefaults();
    }
  }

  addCase(): void {
    this.cases = [...this.cases, this.createCase('', '', 5)];
    this.persistCases();
  }

  removeCase(caseId: string): void {
    this.cases = this.cases.filter((c) => c.id !== caseId);
    this.results = this.results.filter((r) => r.caseId !== caseId);
    this.persistCases();
  }

  async resetDefaults(): Promise<void> {
    this.error = '';
    this.info = '';
    try {
      const docs = await firstValueFrom(this.api.listDocuments(20));
      this.cases = this.defaultCases(docs);
      this.results = [];
      this.persistCases();
      this.info = `Loaded ${this.cases.length} default case(s).`;
    } catch (err: unknown) {
      this.error = this.toErrorMessage(err);
      this.cases = this.defaultCases([]);
      this.results = [];
      this.persistCases();
    }
  }

  onCasesChanged(): void {
    this.persistCases();
  }

  async runAll(): Promise<void> {
    if (this.isRunning) return;

    this.error = '';
    this.info = '';
    this.isRunning = true;
    const startedAtByCase = new Map<string, number>();
    for (const testCase of this.cases) {
      startedAtByCase.set(testCase.id, Date.now());
    }

    this.results = this.cases.map((c) => ({
      caseId: c.id,
      status: 'pending',
      recallHit: false,
      chatHit: false,
      expectedFile: c.expectedFile.trim(),
      recallTopFile: '',
      providerModel: '',
      detail: '',
      durationMs: 0,
    }));

    try {
      const groups = this.groupCasesByExpectedFile(this.cases);
      for (let groupIndex = 0; groupIndex < groups.length; groupIndex++) {
        const group = groups[groupIndex];
        const probes: RecallProbeResult[] = [];

        // Stage 1: recall probe for each case.
        for (const testCase of group) {
          const probe = await this.runRecallProbe(testCase);
          probes.push(probe);
          this.upsertResult({
            caseId: probe.caseId,
            status: probe.recallError ? 'error' : 'running',
            recallHit: probe.recallHit,
            chatHit: false,
            expectedFile: probe.expectedFile,
            recallTopFile: probe.topRecallFile,
            providerModel: 'n/a',
            detail: probe.recallError ? `Recall error: ${probe.recallError}` : 'Recall complete. Waiting for batch chat.',
            durationMs: Date.now() - (startedAtByCase.get(probe.caseId) ?? Date.now()),
          });

          await this.delay(this.perRecallDelayMs);
        }

        // Stage 2: single chat call for this expected-file group.
        const chatOutcome = await this.runBatchChatForGroup(group);
        for (const probe of probes) {
          const result = this.buildResultFromProbe(
            probe,
            chatOutcome,
            Date.now() - (startedAtByCase.get(probe.caseId) ?? Date.now()));
          this.upsertResult(result);
        }

        if (groupIndex < groups.length - 1) {
          await this.delay(chatOutcome.available ? this.interGroupDelayMs : this.providerUnavailableBackoffMs);
        }
      }

      this.info = `Completed ${this.cases.length} case(s) with ${groups.length} batch chat call(s).`;
    } finally {
      this.isRunning = false;
    }
  }

  async autoGenerateCases(): Promise<void> {
    if (this.isRunning) return;

    this.error = '';
    this.info = '';
    try {
      const docs = await firstValueFrom(this.api.listDocuments(20));
      if (docs.length === 0) {
        this.cases = [this.createCase('Upload a document, then auto-generate eval cases.', '', 5)];
        this.results = [];
        this.persistCases();
        this.info = 'No documents found yet. Upload files first, then generate.';
        return;
      }

      const generated: EvalCase[] = [];
      for (const doc of docs.slice(0, 8)) {
        const chunks = await firstValueFrom(this.api.getDocumentChunks(doc.documentId, 80));
        generated.push(...this.buildCasesFromChunks(doc, chunks));
      }

      if (generated.length === 0) {
        this.cases = this.defaultCases(docs);
        this.results = [];
        this.persistCases();
        this.info = 'No chunk content available for generation. Loaded defaults instead.';
        return;
      }

      this.cases = generated.slice(0, 20);
      this.results = [];
      this.persistCases();
      this.info = `Generated ${this.cases.length} section-based case(s) from ${Math.min(docs.length, 8)} document(s).`;
    } catch (err: unknown) {
      this.error = this.toErrorMessage(err);
    }
  }

  passCount(): number {
    return this.results.filter((r) => r.status === 'pass').length;
  }

  failCount(): number {
    return this.results.filter((r) => r.status === 'fail' || r.status === 'error').length;
  }

  partialCount(): number {
    return this.results.filter((r) => r.status === 'partial').length;
  }

  resultFor(caseId: string): EvalResult | undefined {
    return this.results.find((r) => r.caseId === caseId);
  }

  private groupCasesByExpectedFile(cases: EvalCase[]): EvalCase[][] {
    const groups = new Map<string, EvalCase[]>();
    for (const testCase of cases) {
      const expected = this.normalizeFileName(testCase.expectedFile);
      const key = expected || `__case__${testCase.id}`;
      const existing = groups.get(key) ?? [];
      existing.push(testCase);
      groups.set(key, existing);
    }

    return Array.from(groups.values());
  }

  private async runRecallProbe(testCase: EvalCase): Promise<RecallProbeResult> {
    const expected = testCase.expectedFile.trim();
    const question = testCase.question.trim();
    const topK = Math.min(10, Math.max(1, testCase.topK || 5));

    if (!question || !expected) {
      return {
        caseId: testCase.id,
        expectedFile: expected,
        question,
        topK,
        recallHit: false,
        topRecallFile: '',
        recallError: 'Question and expected file are required.',
      };
    }

    try {
      const recall = await this.withRetry(
        () => firstValueFrom(this.api.searchRecall(question, topK)),
        3);
      const expectedNorm = this.normalizeFileName(expected);
      const recallHit = recall.citations.some((c) => this.normalizeFileName(c.fileName) === expectedNorm);
      const topRecallFile = recall.citations.length > 0 ? recall.citations[0].fileName : '';

      return {
        caseId: testCase.id,
        expectedFile: expected,
        question,
        topK,
        recallHit,
        topRecallFile: topRecallFile,
        recallError: '',
      };
    } catch (err: unknown) {
      return {
        caseId: testCase.id,
        expectedFile: expected,
        question,
        topK,
        recallHit: false,
        topRecallFile: '',
        recallError: this.toErrorMessage(err),
      };
    }
  }

  private async runBatchChatForGroup(group: EvalCase[]): Promise<BatchChatOutcome> {
    const valid = group.filter((c) =>
      c.expectedFile.trim().length > 0
      && c.question.trim().length > 0);

    if (valid.length === 0) {
      return {
        available: false,
        chatHit: false,
        guardBlocked: false,
        providerModel: 'n/a',
        detail: 'No valid questions in this batch.',
      };
    }

    const topK = valid.reduce((max, c) => Math.max(max, Math.min(10, Math.max(1, c.topK || 5))), 1);
    const expectedNorm = this.normalizeFileName(valid[0].expectedFile);
    const prompt = this.buildBatchPrompt(valid);

    try {
      const chat = await this.withRetry(
        () => firstValueFrom(this.api.chat(prompt, topK)),
        3);
      const citationHit = chat.citations.some((c) => this.normalizeFileName(c.fileName) === expectedNorm);
      const guardBlocked = chat.provider === 'guard';

      return {
        available: true,
        chatHit: citationHit && !guardBlocked,
        guardBlocked,
        providerModel: `${chat.provider} / ${chat.model}`,
        detail: guardBlocked ? 'Insufficient evidence guard.' : 'Batch chat complete.',
      };
    } catch (err: unknown) {
      const detail = this.toErrorMessage(err);
      if (this.isProviderUnavailableError(err)) {
        return {
          available: false,
          chatHit: false,
          guardBlocked: false,
          providerModel: 'n/a',
          detail: `Chat unavailable: ${detail}`,
        };
      }

      return {
        available: false,
        chatHit: false,
        guardBlocked: false,
        providerModel: 'n/a',
        detail: `Chat failed: ${detail}`,
      };
    }
  }

  private buildBatchPrompt(cases: EvalCase[]): string {
    const lines = cases
      .map((c, i) => `${i + 1}. ${c.question.trim()}`)
      .join('\n');

    return [
      'Answer each numbered question using only the retrieved context snippets.',
      'Keep each answer concise and actionable.',
      'Add citation markers like [1], [2] when evidence is used.',
      'Format exactly as numbered list items.',
      '',
      'Questions:',
      lines,
    ].join('\n');
  }

  private buildResultFromProbe(
    probe: RecallProbeResult,
    chatOutcome: BatchChatOutcome,
    durationMs: number): EvalResult
  {
    if (probe.recallError) {
      return {
        caseId: probe.caseId,
        status: 'error',
        recallHit: false,
        chatHit: false,
        expectedFile: probe.expectedFile,
        recallTopFile: probe.topRecallFile,
        providerModel: 'n/a',
        detail: `Recall error: ${probe.recallError}`,
        durationMs,
      };
    }

    if (!chatOutcome.available) {
      return {
        caseId: probe.caseId,
        status: probe.recallHit ? 'partial' : 'fail',
        recallHit: probe.recallHit,
        chatHit: false,
        expectedFile: probe.expectedFile,
        recallTopFile: probe.topRecallFile,
        providerModel: chatOutcome.providerModel,
        detail: chatOutcome.detail,
        durationMs,
      };
    }

    const chatHit = chatOutcome.chatHit;
    const notes: string[] = [];
    if (!probe.recallHit) notes.push('Recall miss');
    if (!chatHit) notes.push(chatOutcome.guardBlocked ? 'Insufficient evidence guard' : 'Chat citation miss');
    if (notes.length === 0) notes.push('Passed');

    return {
      caseId: probe.caseId,
      status: probe.recallHit && chatHit ? 'pass' : 'fail',
      recallHit: probe.recallHit,
      chatHit,
      expectedFile: probe.expectedFile,
      recallTopFile: probe.topRecallFile,
      providerModel: chatOutcome.providerModel,
      detail: notes.join(' | '),
      durationMs,
    };
  }

  private upsertResult(result: EvalResult): void {
    const existingIndex = this.results.findIndex((r) => r.caseId === result.caseId);
    if (existingIndex === -1) {
      this.results = [...this.results, result];
      return;
    }

    const next = [...this.results];
    next[existingIndex] = result;
    this.results = next;
  }

  private loadCases(): EvalCase[] {
    const raw = this.tryGetLocalStorageItem(this.storageKey);
    if (!raw) return [];

    try {
      const parsed = JSON.parse(raw) as unknown;
      if (!Array.isArray(parsed)) return [];

      const loaded = parsed
        .map((item) => this.parseCase(item))
        .filter((item): item is EvalCase => item !== null);

      return loaded;
    } catch {
      return [];
    }
  }

  private parseCase(value: unknown): EvalCase | null {
    if (!value || typeof value !== 'object') return null;

    const source = value as Record<string, unknown>;
    const question = typeof source['question'] === 'string' ? source['question'] : '';
    const expectedFile = typeof source['expectedFile'] === 'string' ? source['expectedFile'] : '';
    const topKRaw = typeof source['topK'] === 'number' ? source['topK'] : 5;
    const topK = Math.min(10, Math.max(1, Math.trunc(topKRaw)));
    if (!question.trim()) return null;

    const idValue = typeof source['id'] === 'string' && source['id'].trim()
      ? source['id']
      : this.createId();

    return {
      id: idValue,
      question,
      expectedFile,
      topK,
    };
  }

  private persistCases(): void {
    this.setLocalStorageItem(this.storageKey, JSON.stringify(this.cases));
  }

  private normalizeFileName(fileName: string): string {
    return fileName.trim().toLowerCase();
  }

  private defaultCases(documents: DocumentListItem[]): EvalCase[] {
    if (documents.length === 0) {
      return [this.createCase('Summarize the key points from my uploaded document.', '', 5)];
    }

    return documents.slice(0, 10).map((doc) =>
      this.createCase(`Summarize the key points from ${doc.fileName}.`, doc.fileName, 5));
  }

  private buildCasesFromChunks(document: DocumentListItem, chunks: DocumentChunkPreview[]): EvalCase[] {
    const candidates = chunks
      .filter((c) => c.snippet && c.snippet.trim().length > 20)
      .sort((a, b) => a.chunkIndex - b.chunkIndex)
      .slice(0, 6);

    if (candidates.length === 0) {
      return [this.createCase(`Summarize the key points from ${document.fileName}.`, document.fileName, 5)];
    }

    const templates = [
      (topic: string) => `Summarize the section about "${topic}" in ${document.fileName}.`,
      (topic: string) => `What concrete outcomes are described in the "${topic}" section of ${document.fileName}?`,
      (topic: string) => `Rewrite the "${topic}" section from ${document.fileName} with stronger, specific wording.`,
    ];

    const used = new Set<string>();
    const cases: EvalCase[] = [];
    for (let i = 0; i < candidates.length; i++) {
      const chunk = candidates[i];
      const topic = this.toTopic(chunk.snippet);
      const template = templates[i % templates.length];
      const question = template(topic);
      const key = `${document.fileName}|${question}`.toLowerCase();
      if (used.has(key)) continue;

      used.add(key);
      cases.push(this.createCase(question, document.fileName, 5));
      if (cases.length >= 4) break;
    }

    return cases;
  }

  private toTopic(snippet: string): string {
    const cleaned = snippet.replace(/\s+/g, ' ').trim();
    if (!cleaned) return 'the first section';

    const firstSentence = cleaned.split(/[.!?]/, 1)[0].trim();
    const seed = firstSentence || cleaned;
    const words = seed.split(' ').filter(Boolean).slice(0, 8);
    return words.join(' ');
  }

  private createCase(question: string, expectedFile: string, topK: number): EvalCase {
    return {
      id: this.createId(),
      question,
      expectedFile,
      topK,
    };
  }

  private createId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return `case-${Math.random().toString(16).slice(2)}-${Date.now().toString(16)}`;
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => {
      setTimeout(resolve, ms);
    });
  }

  private async withRetry<T>(work: () => Promise<T>, maxAttempts: number): Promise<T> {
    let attempt = 0;
    let lastError: unknown = null;

    while (attempt < maxAttempts) {
      attempt++;
      try {
        return await work();
      } catch (err: unknown) {
        lastError = err;
        if (!this.isTransientError(err) || attempt >= maxAttempts) {
          throw err;
        }

        await this.delay(250 * attempt);
      }
    }

    throw lastError ?? new Error('Evaluation request failed.');
  }

  private isTransientError(err: unknown): boolean {
    const status = this.getStatusCode(err);
    if (status === 429 || status === 503 || status === 504) {
      return true;
    }

    const message = this.toErrorMessage(err).toLowerCase();
    return message.includes('timeout')
      || message.includes('temporar')
      || message.includes('rate limit')
      || message.includes('unavailable');
  }

  private isProviderUnavailableError(err: unknown): boolean {
    const status = this.getStatusCode(err);
    if (status === 503 || status === 429 || status === 504) {
      return true;
    }

    const message = this.toErrorMessage(err).toLowerCase();
    return message.includes('both ai providers failed')
      || message.includes('provider unavailable')
      || message.includes('rate limit')
      || message.includes('temporar');
  }

  private getStatusCode(err: unknown): number | null {
    if (err instanceof HttpErrorResponse) {
      return typeof err.status === 'number' ? err.status : null;
    }

    if (err && typeof err === 'object' && 'status' in err) {
      const maybe = (err as Record<string, unknown>)['status'];
      if (typeof maybe === 'number') {
        return maybe;
      }
    }

    return null;
  }

  private toErrorMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const apiError = this.tryReadApiErrorText(err.error);
      if (apiError) {
        return `HTTP ${err.status}: ${apiError}`;
      }

      if (err.message) {
        return err.message;
      }
    }

    if (err instanceof Error && err.message) {
      return err.message;
    }

    if (err && typeof err === 'object') {
      const apiError = this.tryReadApiErrorText((err as Record<string, unknown>)['error']);
      if (apiError) {
        return apiError;
      }

      const message = (err as Record<string, unknown>)['message'];
      if (typeof message === 'string' && message.trim()) {
        return message;
      }
    }

    return 'Evaluation request failed.';
  }

  private tryReadApiErrorText(value: unknown): string | null {
    if (typeof value === 'string' && value.trim()) {
      return value;
    }

    if (!value || typeof value !== 'object') {
      return null;
    }

    const source = value as Record<string, unknown>;
    const fields = [source['error'], source['detail'], source['title'], source['message']];
    for (const field of fields) {
      if (typeof field === 'string' && field.trim()) {
        return field;
      }
    }

    return null;
  }

  private tryGetLocalStorageItem(key: string): string | null {
    if (typeof localStorage === 'undefined') return null;
    return localStorage.getItem(key);
  }

  private setLocalStorageItem(key: string, value: string): void {
    if (typeof localStorage === 'undefined') return;
    localStorage.setItem(key, value);
  }
}
