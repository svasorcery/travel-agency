import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { providePrimeNG } from 'primeng/config';
import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';
import { appRoutes } from './app.routes';

const TravelPreset = definePreset(Aura, { /* customizations TBD per UI design */ });

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(appRoutes),
    provideHttpClient(withFetch()),
    providePrimeNG({ theme: { preset: TravelPreset, options: { darkModeSelector: '.dark' } } }),
  ],
};
