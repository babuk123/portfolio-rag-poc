import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChatResponse, ChatTurn, CustomerListItem, Portfolio, Review } from './models';

/** All calls go to /api, which the dev server proxies to the .NET API (see proxy.conf.json). */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  customers(): Observable<CustomerListItem[]> {
    return this.http.get<CustomerListItem[]>('/api/customers');
  }

  portfolio(customerId: string): Observable<Portfolio> {
    return this.http.get<Portfolio>(`/api/customers/${customerId}`);
  }

  reviews(customerId: string): Observable<Review[]> {
    return this.http.get<Review[]>(`/api/customers/${customerId}/reviews`);
  }

  requestReview(customerId: string, requestedBy: string, advisorNotes: string): Observable<Review> {
    return this.http.post<Review>(`/api/customers/${customerId}/reviews`, { requestedBy, advisorNotes });
  }

  indexSamplePolicy(): Observable<{ blobName: string; chunks: number }> {
    return this.http.post<{ blobName: string; chunks: number }>('/api/documents/sample-policy', {});
  }

  ask(customerId: string, question: string, history: ChatTurn[]): Observable<ChatResponse> {
    return this.http.post<ChatResponse>('/api/chat', { customerId, question, history });
  }

  reviewPdfUrl(reviewId: string): string {
    return `/api/reviews/${reviewId}/pdf`;
  }

  fileUrl(blobName: string): string {
    return `/api/files?blobName=${encodeURIComponent(blobName)}`;
  }
}
