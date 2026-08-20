import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject, OnChanges, SimpleChanges } from '@angular/core';
import { finalize } from 'rxjs';
import { LogFileEntry, LogsService } from '../../../../services/logs.service';
import { ToastService } from '../../../../services/toast.service';

@Component({
  selector: 'app-logs-panel',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './logs.component.html',
})
export class LogsComponent implements OnChanges {
  @Input() isOpen = false;
  @Output() close = new EventEmitter<void>();

  #logsService = inject(LogsService);
  #toastService = inject(ToastService);

  logs: LogFileEntry[] = [];
  isLoadingLogs = false;

  ngOnChanges(changes: SimpleChanges) {
    if (changes['isOpen']?.currentValue === true) {
      this.loadLogs();
    }
  }

  loadLogs() {
    this.isLoadingLogs = true;

    this.#logsService.getLogs()
      .pipe(
        finalize(() => {
          // Always clear the loading state, including unexpected response/stream failures.
          this.isLoadingLogs = false;
        })
      )
      .subscribe({
        next: (response) => {
          // The API may return either a raw array or a wrapped payload
          // (for example { logs: [...] } / { files: [...] } / { data: [...] }).
          // Normalize it before mapping so a valid HTTP response can never
          // leave the panel permanently stuck on "Loading logs...".
          this.logs = this.normalizeLogs(response);
        },
        error: () => {
          this.logs = [];
          this.#toastService.error('Unable to load logs at the moment.');
        }
      });
  }

  downloadLog(fileName: string) {
    this.#logsService.downloadLog(fileName).subscribe({
      next: (response) => {
        const blob = new Blob([response], { type: 'text/plain' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(link.href);
      },
      error: () => {
        this.#toastService.error('Unable to download the selected log.');
      }
    });
  }

  downloadAllLogs() {
    this.#logsService.downloadAllLogs().subscribe({
      next: (response) => {
        const blob = new Blob([response], { type: 'application/zip' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = `Logs_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '_')}.zip`;
        link.click();
        URL.revokeObjectURL(link.href);
      },
      error: () => {
        this.#toastService.error('Unable to download all logs.');
      }
    });
  }

  onClose() {
    this.close.emit();
  }

  private normalizeLogs(response: unknown): LogFileEntry[] {
    const payload = response as any;

    const entries = Array.isArray(payload)
      ? payload
      : Array.isArray(payload?.logs)
        ? payload.logs
        : Array.isArray(payload?.Logs)
          ? payload.Logs
          : Array.isArray(payload?.files)
            ? payload.files
            : Array.isArray(payload?.Files)
              ? payload.Files
              : Array.isArray(payload?.data)
                ? payload.data
                : Array.isArray(payload?.Data)
                  ? payload.Data
                  : Array.isArray(payload?.items)
                    ? payload.items
                    : Array.isArray(payload?.Items)
                      ? payload.Items
                      : [];

    return entries.map((log: any) => ({
      name: log?.name ?? log?.Name ?? '',
      size: log?.size ?? log?.Size ?? 0,
      created: log?.created ?? log?.Created ?? '',
      modified: log?.modified ?? log?.Modified ?? '',
    }));
  }
}
