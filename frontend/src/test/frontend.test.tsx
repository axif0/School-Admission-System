import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StudentIdBar } from '../components/StudentIdBar';
import { getErrorMessage } from '../i18n/messages';

describe('Frontend Unit Tests', () => {
  it('Validates 17-digit student id and renders error state', () => {
    const handleChange = vi.fn();

    // Render with 10 digits (invalid)
    const { rerender } = render(
      <StudentIdBar
        studentId="1234567890"
        onStudentIdChange={handleChange}
        isValid={false}
      />
    );

    expect(screen.getByText(/10\/17 digits/i)).toBeInTheDocument();
    expect(screen.getByText(/must be exactly 17 digits/i)).toBeInTheDocument();

    // Rerender with 17 digits (valid)
    rerender(
      <StudentIdBar
        studentId="20152691234567890"
        onStudentIdChange={handleChange}
        isValid={true}
      />
    );

    expect(screen.getByText(/✓ Valid \(17 digits\)/i)).toBeInTheDocument();
  });

  it('Maps error codes to friendly human language', () => {
    expect(getErrorMessage('NO_SEATS_AVAILABLE')).toBe(
      'All seats in this class are currently held or confirmed. No seats available.'
    );
    expect(getErrorMessage('ALREADY_APPLIED')).toBe(
      'You already have an active hold or confirmed admission in this class.'
    );
    expect(getErrorMessage('HOLD_EXPIRED')).toBe(
      'This seat hold has expired. The seat is no longer reserved.'
    );
    expect(getErrorMessage('TEMPORARILY_BUSY')).toBe(
      'The system is currently handling high traffic. Please try again in a few moments.'
    );
    expect(getErrorMessage('UNKNOWN_CODE', 'Custom message')).toBe('Custom message');
  });
});
