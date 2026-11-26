export interface CustomerListItem {
  id: string;
  fullName: string;
  riskProfile: string;
  advisorName: string;
  totalValue: number;
}

export interface Holding {
  symbol: string;
  name: string;
  assetClass: string;
  quantity: number;
  costPerShare: number;
  currentPrice: number;
  marketValue: number;
  gainLoss: number;
  weightPct: number;
}

export interface Allocation {
  assetClass: string;
  marketValue: number;
  weightPct: number;
  targetPct: number;
}

export interface Portfolio {
  customerId: string;
  fullName: string;
  email: string;
  accountNumberMasked: string;
  riskProfile: string;
  advisorName: string;
  totalValue: number;
  totalCost: number;
  totalGainLoss: number;
  holdings: Holding[];
  allocation: Allocation[];
}

export interface Review {
  id: string;
  customerId: string;
  requestedBy: string;
  advisorNotes?: string;
  status: 'Requested' | 'Generated' | 'Indexed' | 'Failed';
  blobName?: string;
  chunksIndexed: number;
  error?: string;
  createdUtc: string;
}

export interface ChatTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface Citation {
  number: number;
  documentName: string;
  pageNumber: number;
  blobName: string;
  docType: string;
  snippet: string;
  score: number;
}

export interface ChatResponse {
  answer: string;
  citations: Citation[];
  grounded: boolean;
  chunksRetrieved: number;
}

/** A chat message as shown in the UI (assistant messages carry their citations). */
export interface ChatMessageView extends ChatTurn {
  citations?: Citation[];
  grounded?: boolean;
}
