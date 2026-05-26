import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Pagination } from '@/shared/components/Pagination';

describe('Pagination', () => {
  it('renders screenshot-aligned page controls, result count, and page size selector', async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    const onPageSizeChange = vi.fn();

    render(
      <Pagination
        page={1}
        pageSize={20}
        shownStart={1}
        shownEnd={20}
        totalCount={1248}
        totalPages={63}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
      />
    );

    expect(screen.getByText('Showing 1 to 20 of 1,248 results')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Go to page 1' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Go to page 2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to page 3' })).toBeInTheDocument();
    expect(screen.getByText('...')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to page 63' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Rows per page' })).toHaveValue('20');

    await user.click(screen.getByRole('button', { name: 'Go to page 2' }));
    expect(onPageChange).toHaveBeenCalledWith(2);

    await user.selectOptions(screen.getByRole('combobox', { name: 'Rows per page' }), '50');
    expect(onPageSizeChange).toHaveBeenCalledWith(50);
  });
});
