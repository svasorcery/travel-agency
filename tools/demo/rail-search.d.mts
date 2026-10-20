import type { IncomingMessage, ServerResponse } from 'node:http';
import type { RailRequest, RailSearchResult, RailSuggestions } from '../../shared/ts/api-client/src/rail.types';
export function fictionalRailSuggestions(q: string, now?: number, preset?: string): RailSuggestions;
export function fictionalRailSearch(request: RailRequest, now?: number, preset?: string): RailSearchResult;
export function createRailDemo(): (request: IncomingMessage, response: ServerResponse, url: URL) => Promise<boolean>;
