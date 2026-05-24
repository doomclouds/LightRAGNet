import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { App } from '@/app/App';

describe('AppLayout', () => {
  it('renders the app banner and document navigation links', () => {
    render(<App />);

    expect(screen.getByRole('banner')).toHaveTextContent('LightRAGNet');

    const navigation = within(screen.getByRole('navigation', { name: 'Primary' }));
    expect(navigation.getByRole('link', { name: 'Documents' })).toHaveAttribute('href', '/documents');
    expect(navigation.getByRole('link', { name: 'Upload' })).toHaveAttribute('href', '/documents/upload');
  });
});
