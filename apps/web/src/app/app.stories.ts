import { provideRouter } from '@angular/router';
import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { expect } from 'storybook/test';
import { App } from './app';

const meta: Meta<App> = {
  component: App,
  title: 'App',
  decorators: [applicationConfig({ providers: [provideRouter([])] })],
};
export default meta;

type Story = StoryObj<App>;

export const StatusShell: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByRole('link', { name: 'Travel Platform' })).toBeVisible();
  },
};
