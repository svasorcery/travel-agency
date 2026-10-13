import type { SupportCategory } from './support.types';
export type SupportSuggestedCategory = Exclude<SupportCategory, 'Unclassified'>;
export const supportClassificationReasons = [
  'Disabled',
  'Ready',
  'InputTooLarge',
  'Unavailable',
  'Uncertain',
  'InvalidResponse',
  'LowConfidence',
  'IdentityMismatch',
  'Busy',
] as const;
export type SupportClassificationReason = (typeof supportClassificationReasons)[number];
export interface SupportUrgencySuggestion {
  kind: 'Unknown' | 'KeepCurrent' | 'High';
  nextScheduledDepartureAt: string | null;
  observedAt: string | null;
  streamVersion: number | null;
  ruleVersion: 'support-urgency-v1';
}
export interface SupportClassificationSuggestion {
  caseId: string;
  requestId: string;
  capturedRevision: number;
  generatedAt: string;
  expiresAt: string;
  status: 'Disabled' | 'ManualRequired' | 'Candidate';
  reason: SupportClassificationReason;
  category: SupportSuggestedCategory | null;
  queue: SupportSuggestedCategory | null;
  servingProfileId: string | null;
  questionVersion: 'support-category-v1' | null;
  urgency: SupportUrgencySuggestion;
}
