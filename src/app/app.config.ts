import {
  APP_INITIALIZER,
  ApplicationConfig,
  importProvidersFrom,
  isDevMode,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';

import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { Config } from '../assets/environments/config';
import { paginationRequestFactory } from '../factories/pagination-request-factory';
import {
  API_CONSTANTS,
  CONFIG_FILE_PATH,
  PAGINATION_REQUEST,
} from '../injectors/common-injector';
import { authInterceptor } from '../interceptor/auth-interceptor';
import { routes } from './app.routes';
import { AuthenticationService } from './components/authentication/services/authentication-service';
import { ConfigService } from './services/app-config-service';
import { ApiConstants } from '../api-constants';
import { environment } from '../assets/environments/environment';
import { refreshTokenInterceptor } from '../interceptor/refresh-interceptor';
import { provideServiceWorker } from '@angular/service-worker';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor, refreshTokenInterceptor]),
    ),
    importProvidersFrom(
      AuthenticationService, // Import any additional modules here, e.g., HttpClientModule, FormsModule, etc.
    ),
    {
      provide: PAGINATION_REQUEST,
      useFactory: paginationRequestFactory,
    },
    {
      provide: CONFIG_FILE_PATH,
      useValue: '/assets/environments/config.json',
    },
    {
      provide: APP_INITIALIZER,
      useFactory: (configService: ConfigService) => async () => {
        await configService.load();
      },
      deps: [ConfigService],
      multi: true,
    },
    {
      provide: Config,
      useFactory: (configService: ConfigService) => {
        return isDevMode() ? (environment as Config) : configService.value;
      },
      deps: [ConfigService],
    },
    {
      provide: API_CONSTANTS,
      useClass: ApiConstants,
    },
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    })
  ],
};
