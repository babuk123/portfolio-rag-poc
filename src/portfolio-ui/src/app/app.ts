import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from './api.service';
import { ChatMessageView, CustomerListItem, Portfolio, Review } from './models';

@Component({
  selector: 'app-root',
  imports: [FormsModule, CurrencyPipe, DecimalPipe, DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  protected readonly api = inject(ApiService);

  // ---- state (signals keep the UI in sync without zone.js) ----
  readonly customers = signal<CustomerListItem[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly portfolio = signal<Portfolio | null>(null);
  readonly reviews = signal<Review[]>([]);
  readonly messages = signal<ChatMessageView[]>([]);
  readonly generating = signal(false);
  readonly asking = signal(false);
  readonly notice = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  advisorName = 'Alex Carter';
  advisorNotes = '';
  question = '';

  readonly suggestions = [
    'Does this portfolio need rebalancing under our policy?',
    'Which holdings have unrealized losses?',
    'Is any single holding above the concentration limit?',
    'What advisory fee applies to this account?',
  ];

  readonly hasIndexedReview = computed(() => this.reviews().some(r => r.status === 'Indexed'));

  ngOnInit(): void {
    this.api.customers().subscribe({
      next: list => {
        this.customers.set(list);
        if (list.length) this.select(list[0].id);
      },
      error: e => this.fail('Could not load customers. Is the API running on port 5017?', e),
    });
  }

  select(customerId: string): void {
    this.selectedId.set(customerId);
    this.portfolio.set(null);
    this.messages.set([]);
    this.error.set(null);
    this.api.portfolio(customerId).subscribe({ next: p => this.portfolio.set(p), error: e => this.fail('Could not load portfolio.', e) });
    this.loadReviews();
  }

  loadReviews(): void {
    const id = this.selectedId();
    if (!id) return;
    this.api.reviews(id).subscribe({ next: r => this.reviews.set(r), error: e => this.fail('Could not load reviews.', e) });
  }

  generateReview(): void {
    const id = this.selectedId();
    if (!id || this.generating()) return;
    this.generating.set(true);
    this.notice.set('Generating PDF, saving to Blob Storage and indexing in Azure AI Search...');
    this.api.requestReview(id, this.advisorName, this.advisorNotes).subscribe({
      next: r => {
        this.generating.set(false);
        this.notice.set(r.status === 'Indexed'
          ? `Review ready: ${r.chunksIndexed} chunks indexed. You can now ask the assistant about it.`
          : `Review status: ${r.status}. ${r.error ?? ''}`);
        this.advisorNotes = '';
        this.loadReviews();
      },
      error: e => { this.generating.set(false); this.fail('Review generation failed.', e); },
    });
  }

  indexPolicy(): void {
    this.notice.set('Indexing sample policy WM-104...');
    this.api.indexSamplePolicy().subscribe({
      next: r => this.notice.set(`Policy indexed (${r.chunks} chunks). The assistant can now quote firm rules.`),
      error: e => this.fail('Policy indexing failed. Check the Azure settings in the API.', e),
    });
  }

  ask(text?: string): void {
    const question = (text ?? this.question).trim();
    const id = this.selectedId();
    if (!question || !id || this.asking()) return;

    const history = this.messages().map(m => ({ role: m.role, content: m.content }));
    this.messages.update(m => [...m, { role: 'user', content: question }]);
    this.question = '';
    this.asking.set(true);

    this.api.ask(id, question, history).subscribe({
      next: res => {
        this.messages.update(m => [...m, { role: 'assistant', content: res.answer, citations: res.citations, grounded: res.grounded }]);
        this.asking.set(false);
      },
      error: e => {
        this.asking.set(false);
        this.messages.update(m => [...m, { role: 'assistant', content: 'Sorry, the assistant failed. Check the API log.', grounded: false }]);
        this.fail('Chat request failed.', e);
      },
    });
  }

  driftClass(weight: number, target: number): string {
    const d = Math.abs(weight - target);
    return d > 5 ? 'bad' : d > 3 ? 'warn' : 'ok';
  }

  private fail(message: string, e: unknown): void {
    const detail = e instanceof HttpErrorResponse ? ` (${e.status} ${e.statusText})` : '';
    this.error.set(message + detail);
    this.notice.set(null);
    console.error(e);
  }
}
