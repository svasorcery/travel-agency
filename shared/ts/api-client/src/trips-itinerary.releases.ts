export const tripItineraryReleases = {
  moscow: {
    datasetId: 'moscow-outdoor',
    version: '1',
    hash: '631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08',
    sourceUrl: 'https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz',
    profile: 'profile-v1',
    offer: '/trips-data/moscow-outdoor/1/moscow-v1.manifest.json',
    bounds: [55.72, 37.57, 55.78, 37.66],
  },
  'saint-petersburg': {
    datasetId: 'saint-petersburg-outdoor',
    version: '1',
    hash: 'dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12',
    sourceUrl: 'https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz',
    profile: 'spb-outdoor-profile-v1',
    offer: '/trips-data/saint-petersburg-outdoor/1/saint-petersburg-v1.manifest.json',
    bounds: [59.923, 30.29, 59.947, 30.346],
  },
} as const;
export type TripItineraryCity = keyof typeof tripItineraryReleases;
export function tripItineraryDataOffer(destinationId: string): string | null {
  return destinationId === 'moscow' || destinationId === 'saint-petersburg'
    ? tripItineraryReleases[destinationId].offer
    : null;
}
