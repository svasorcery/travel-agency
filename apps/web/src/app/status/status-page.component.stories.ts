import type { Meta, StoryObj } from '@storybook/angular';
import { StatusPageComponent } from './status-page.component';

const meta: Meta<StatusPageComponent> = {
  title: 'Pages/StatusPage',
  component: StatusPageComponent,
};
export default meta;

type Story = StoryObj<StatusPageComponent>;

export const Default: Story = {};
