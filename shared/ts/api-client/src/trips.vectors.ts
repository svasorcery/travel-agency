// Fictional contract vector from design section 4.1. No created resource or supplier fact.
export const tripsExample = {
  tripId: '11111111-1111-4111-8111-111111111111',
  revision: '2',
  title: 'Выходной в Петербурге',
  brief: {
    destinationId: 'saint-petersburg',
    originId: 'moscow',
    startDate: '2026-11-14',
    endDate: '2026-11-14',
    adults: 2,
    currency: 'RUB',
    totalBudget: '15000.00',
    interests: ['architecture', 'food'],
    pace: 'relaxed',
  },
  destination: {
    destinationId: 'saint-petersburg',
    displayName: 'Санкт-Петербург',
    timeZone: 'Europe/Moscow',
    catalogueVersion: '1',
  },
  createdAt: '2026-10-20T09:00:00Z',
  updatedAt: '2026-10-20T09:05:00Z',
  days: [
    {
      date: '2026-11-14',
      blocks: [
        {
          blockId: '22222222-2222-4222-8222-222222222222',
          title: 'Прогулка',
          note: null as string | null,
          startTime: '10:00' as string | null,
          endTime: '11:30' as string | null,
          estimate: { amount: '500.00', currency: 'RUB' } as { amount: string; currency: string } | null,
        },
      ],
    },
  ],
  knownUserEstimateSubtotal: '500.00',
  unknownEstimateCount: 0,
  warnings: [] as string[],
};
