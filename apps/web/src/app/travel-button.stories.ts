import type { Meta, StoryObj } from '@storybook/angular';
import { TravelButton } from '@travel/ui-kit';
import { expect } from 'storybook/test';

const meta: Meta = {
  title: 'UI/Button',
  render: () => ({
    imports: [TravelButton],
    template: '<button travelButton>Search flights</button>',
  }),
};

export default meta;

type Story = StoryObj;

export const Primary: Story = {
  play: async ({ canvas }) => {
    const button = canvas.getByRole('button', { name: 'Search flights' });

    await expect(button).toHaveAttribute('type', 'button');
    await expect(button).toHaveClass('bg-primary');
  },
};
