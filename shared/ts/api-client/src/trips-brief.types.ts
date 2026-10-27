import type { TripCity, TripInterest, TripPace } from './trips.types';
export const tripBriefSlots = [
  'destinationId',
  'startDate',
  'endDate',
  'durationDays',
  'adults',
  'interests',
  'pace',
] as const;
export type TripBriefSlot = (typeof tripBriefSlots)[number];
export type TripBriefZone = 'Europe/Moscow' | 'Europe/Kaliningrad';
export type TripBriefFieldValue = TripCity | string | number | TripInterest[] | TripPace;
export interface TripBriefEvidence {
  start: number;
  end: number;
}
export interface TripBriefField {
  state: 'extracted' | 'missing' | 'ambiguous' | 'unsupported' | 'invalid';
  value: TripBriefFieldValue | null;
  evidence: TripBriefEvidence[];
  alternatives: TripBriefFieldValue[];
}
export interface TripBriefCapability {
  candidateVersion: 1;
  status: 'configured' | 'unconfigured';
  processorId: string | null;
  modelId: string | null;
  processingPolicyVersion: string | null;
}
export interface TripBriefRequest {
  requestId: string;
  text: string;
  locale: 'ru';
  referenceZone: TripBriefZone | null;
  processingPolicyVersion: string;
}
export interface TripBriefCandidate {
  candidateVersion: 1;
  requestId: string;
  processingPolicyVersion: string;
  referenceInstant: string;
  referenceZone: TripBriefZone | null;
  referenceLocalDate: string | null;
  expiresAt: string;
  fields: Record<TripBriefSlot, TripBriefField>;
  issues: { code: string; slots: TripBriefSlot[]; evidence: TripBriefEvidence[] }[];
}
