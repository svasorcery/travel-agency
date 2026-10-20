import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';
import { installTravelFramePolicy } from './app/auth/travel-frame-policy';

installTravelFramePolicy(document, window.location, window.__TRAVEL_FLIGHTS_AUTH__ ?? null);

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
