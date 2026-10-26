import '@angular/compiler';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import * as client from './index';
import { tripItineraryReleases } from './trips-itinerary.releases';

const fixture = (name: string) =>
  JSON.parse(
    readFileSync(new URL(`../../../../tests/fixtures/trips-itinerary-v2/${name}.json`, import.meta.url), 'utf8'),
  );
describe('explicit Trips v2 strict wire', () => {
  it.each(['moscow', 'saint-petersburg'] as const)(
    'decodes bounded fictional vectors for %s under its exact release tuple',
    (city) => {
      for (const name of ['candidate', 'partial', 'empty']) {
        const result = client.decodeTripItineraryCandidateV2(JSON.stringify(fixture(name + '-' + city)));
        expect(result.candidateVersion).toBe(2);
        expect(result.destinationId).toBe(city);
      }
      const ready = client.decodeTripItineraryAvailabilityV2(JSON.stringify(fixture('ready-' + city)));
      expect(ready.status).toBe('ready');
      expect(ready.destinationId).toBe(city);
    },
  );
  it('keeps v1 and v2 dispatch strict, with no downgrade or silent widening', () => {
    expect(() => client.decodeTripItineraryCandidate(JSON.stringify(fixture('candidate-saint-petersburg')))).toThrow(
      client.TripsItineraryContractError,
    );
    const legacy = readFileSync(
      new URL('../../../../tests/fixtures/trips-itinerary-v1/candidate.json', import.meta.url),
      'utf8',
    );
    expect(() => client.decodeTripItineraryCandidateV2(legacy)).toThrow(client.TripsItineraryContractError);
  });
  it('decodes only the exact source-free catalogue unsupported availability', () => {
    const input = {
      candidateVersion: 2,
      status: 'unsupported',
      destinationId: 'kaliningrad',
      reason: 'destinationUnsupported',
    };
    expect(client.decodeTripItineraryAvailabilityV2(JSON.stringify(input))).toEqual(input);
    expect(() =>
      client.decodeTripItineraryAvailabilityV2(JSON.stringify({ ...input, destinationId: 'moscow' })),
    ).toThrow(client.TripsItineraryContractError);
    expect(() =>
      client.decodeTripItineraryAvailabilityV2(JSON.stringify({ ...input, datasetId: 'moscow-outdoor' })),
    ).toThrow(client.TripsItineraryContractError);
  });
  it.each(['dataset', 'version', 'hash', 'profile', 'source', 'offer', 'coordinate', 'protocol'] as const)(
    'rejects a mixed city %s',
    (kind) => {
      const value = fixture('candidate-saint-petersburg'),
        moscow = tripItineraryReleases.moscow;
      if (kind === 'dataset') value.datasetId = moscow.datasetId;
      if (kind === 'version') value.datasetVersion = 'next';
      if (kind === 'hash') value.datasetHash = moscow.hash;
      if (kind === 'profile') value.modelSource.profileVersion = moscow.profile;
      if (kind === 'source') value.modelSource.sourceUrl = moscow.sourceUrl;
      if (kind === 'offer') value.derivedDataOffer = moscow.offer;
      if (kind === 'coordinate') value.days[0].blocks[0].latitude = 55.75;
      if (kind === 'protocol') value.candidateVersion = 3;
      expect(() => client.decodeTripItineraryCandidateV2(JSON.stringify(value))).toThrow(
        client.TripsItineraryContractError,
      );
    },
  );
  it('pins both public wire hashes to their actual offered source bytes', () => {
    for (const [city, release] of Object.entries(tripItineraryReleases)) {
      const bytes = readFileSync(
        new URL(
          `../../../../modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/${release.datasetId}/1/${city}-v1.json`,
          import.meta.url,
        ),
      );
      expect(createHash('sha256').update(bytes).digest('hex')).toBe(release.hash);
    }
  });
});
