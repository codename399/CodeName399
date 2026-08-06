import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_CONSTANTS } from '../../injectors/common-injector';

export interface LogFileEntry {
  name: string;
  size: number;
  created: string;
  modified: string;
}

@Injectable({
  providedIn: 'root',
})
export class LogsService {
  #httpClient = inject(HttpClient);
  #apiConstants = inject(API_CONSTANTS);

  getLogs() {
    return this.#httpClient.get<LogFileEntry[]>(
      this.#apiConstants.getUrl(this.#apiConstants.logs, true)
    );
  }

  downloadLog(fileName: string) {
    const url = `${this.#apiConstants.getUrl(this.#apiConstants.downloadLog, true)}/${encodeURIComponent(fileName)}`;

    return this.#httpClient.get(url, {
      responseType: 'blob',
    });
  }

  downloadAllLogs() {
    return this.#httpClient.get(
      this.#apiConstants.getUrl(this.#apiConstants.downloadAllLogs, true),
      {
        responseType: 'blob',
      }
    );
  }
}
