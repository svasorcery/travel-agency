import type { FlightOffer, FlightRankingEntry, FlightSearchResponse } from '@travel/api-client';

interface BaseOfferView {
  id: string;
  provider: string;
  price: string;
  ranking?: { rank: number; currency: string; duration: string; transfers: string; priceNote: string };
  rankingGroup?: string;
}

export interface BookableOfferView extends BaseOfferView {
  kind: 'bookable';
  totalDuration: string;
  slices: {
    origin: string;
    destination: string;
    departure: string;
    arrival: string;
    duration: string;
    transferCount: number;
    segments: { route: string; departure: string; arrival: string; flight: string }[];
  }[];
}

export interface PartnerOfferView extends BaseOfferView {
  kind: 'partner';
  partnerName: string;
  direction: string;
}

export type FlightOfferView = BookableOfferView | PartnerOfferView;

function formatDuration(value: string): string {
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?$/.exec(value);
  if (!match) return value;
  const hours = Number(match[1] ?? 0) * 24 + Number(match[2]);
  const minutes = Number(match[3]);
  return `${hours} ч ${minutes} мин`;
}

export function formatOffsetTime(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):\d{2}(?:\.\d+)?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) return value;
  const offset = match[6] === 'Z' ? '+00:00' : match[6];
  return `${match[3]}.${match[2]}.${match[1]}, ${match[4]}:${match[5]} UTC${offset}`;
}

export function formatFlightPrice(amount: number, currency: string): string {
  return `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(amount)} ${currency}`;
}

export function toFlightOfferView(offer: FlightOffer): FlightOfferView {
  const common: BaseOfferView = {
    id: offer.id,
    provider: offer.provider,
    price: formatFlightPrice(offer.totalAmount, offer.currency),
  };
  if (offer.providerOfferRef === null) {
    const firstSlice = offer.itinerary.slices[0];
    return {
      ...common,
      kind: 'partner',
      partnerName: offer.partnerName,
      direction: `${firstSlice.origin} → ${firstSlice.destination}`,
    };
  }
  return {
    ...common,
    kind: 'bookable',
    totalDuration: formatDuration(offer.itinerary.totalDuration),
    slices: offer.itinerary.slices.map((slice) => ({
      origin: slice.origin,
      destination: slice.destination,
      departure: formatOffsetTime(slice.segments[0].departAt),
      arrival: formatOffsetTime(slice.segments[slice.segments.length - 1].arriveAt),
      duration: formatDuration(slice.duration),
      transferCount: slice.segments.length - 1,
      segments: slice.segments.map((segment) => ({
        route: `${segment.origin} → ${segment.destination}`,
        departure: formatOffsetTime(segment.departAt),
        arrival: formatOffsetTime(segment.arriveAt),
        flight: `${segment.carrierCode} ${segment.flightNumber}`,
      })),
    })),
  };
}

export function summarizeSearchResponse(response: FlightSearchResponse): {
  offers: FlightOfferView[];
  partial: boolean;
  currencyMismatch: boolean;
  rankingAvailable: boolean;
} {
  const entries = new Map(response.ranking?.entries.map((entry) => [entry.offerId, entry]));
  const multipleCurrencies = new Set(response.offers.map((offer) => offer.currency)).size > 1;
  return {
    offers: response.offers.map((offer, index) => {
      const view = toFlightOfferView(offer);
      const entry = entries.get(offer.id);
      if (!entry) return view;
      return {
        ...view,
        ranking: rankingView(entry, offer),
        rankingGroup:
          (multipleCurrencies || offer.currency !== response.ranking?.requestedCurrency) &&
          (index === 0 || response.offers[index - 1].currency !== offer.currency)
            ? offer.currency
            : undefined,
      };
    }),
    partial: response.partialFailures.length > 0,
    currencyMismatch: response.offers.some(
      (offer) => offer.currency !== (response.ranking?.requestedCurrency ?? 'RUB'),
    ),
    rankingAvailable: response.ranking !== undefined && response.ranking !== null,
  };
}

function rankingView(entry: FlightRankingEntry, offer: FlightOffer): NonNullable<FlightOfferView['ranking']> {
  const seconds = entry.durationSeconds;
  const price = (amount: number, currency: string) =>
    `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 20 }).format(amount)} ${currency}`;
  const displayed = price(offer.totalAmount, offer.currency);
  return {
    rank: entry.rank,
    currency: entry.currency,
    duration:
      seconds === null
        ? 'Длительность неизвестна'
        : `${Math.floor(seconds / 3600)} ч ${Math.floor((seconds % 3600) / 60)} мин${seconds % 60 ? ` ${seconds % 60} с` : ''}`,
    transfers: entry.transfers === null ? 'Пересадки неизвестны' : `Пересадок: ${entry.transfers}`,
    priceNote:
      entry.priceState === 'fx-unavailable'
        ? `Курс недоступен: сравнение только внутри ${entry.currency}. Для сравнения: ${displayed}.`
        : entry.priceState === 'converted'
          ? `Для сравнения: ${displayed}. Исходная цена: ${price(entry.sourceAmount, entry.sourceCurrency)}. Курс на момент поиска.`
          : `${offer.providerOfferRef === null ? 'Цена по данным партнёра' : 'Для сравнения'}: ${displayed}.`,
  };
}
