import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { StockCapture } from '../components/simulation/models';

export interface SavedSimulationSummary {
  id: string;
  name: string;
  symbol: string;
  source: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  sizeBytes: number;
}

interface LiveSimulationResponse {
  generatedAtUtc: string;
  capture: StockCapture;
}

@Injectable({ providedIn: 'root' })
export class SimulationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/simulation';

  getLive(symbolOrToken: string): Observable<LiveSimulationResponse> {
    return this.http.get<LiveSimulationResponse>(`${this.baseUrl}/live/${encodeURIComponent(symbolOrToken)}`);
  }

  save(request: { name: string; symbol: string; source: string; data: StockCapture }): Observable<SavedSimulationSummary> {
    return this.http.post<SavedSimulationSummary>(`${this.baseUrl}/saved`, request);
  }

  listSaved(limit = 100): Observable<SavedSimulationSummary[]> {
    return this.http.get<SavedSimulationSummary[]>(`${this.baseUrl}/saved`, {
      params: new HttpParams().set('limit', String(limit)),
    });
  }

  loadSaved(id: string): Observable<StockCapture> {
    return this.http.get<StockCapture>(`${this.baseUrl}/saved/${encodeURIComponent(id)}`);
  }
}
