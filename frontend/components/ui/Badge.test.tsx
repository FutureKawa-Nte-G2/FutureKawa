import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Badge } from './Badge';

describe('Badge', () => {
  it('displays the French label for each status', () => {
    const { rerender } = render(<Badge status='compliant' />);
    expect(screen.getByText('conforme')).toBeInTheDocument();

    rerender(<Badge status='expired' />);
    expect(screen.getByText('périmé')).toBeInTheDocument();
  });

  it('applies the correct color classes per status', () => {
    const { rerender } = render(<Badge status='compliant' />);
    expect(screen.getByText('conforme')).toHaveClass(
      'text-status-compliant-text',
      'bg-status-compliant-bg'
    );

    rerender(<Badge status='expired' />);
    expect(screen.getByText('périmé')).toHaveClass(
      'text-status-expired-text',
      'bg-status-expired-bg'
    );
  });
});
