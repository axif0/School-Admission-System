import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StudentIdBar } from '../components/StudentIdBar';
import { getErrorMessage, messages } from '../i18n/messages';

describe('Frontend Unit Tests', () => {
  it('Validates 17-digit student id and renders error state', () => {
    const handleChange = vi.fn();

    // Render with 10 digits (invalid)
    const { rerender } = render(
      <StudentIdBar
        studentId="1234567890"
        onStudentIdChange={handleChange}
        isValid={false}
        t={messages.en}
      />
    );

    expect(screen.getByText(/10 \/ 17/i)).toBeInTheDocument();
    expect(screen.getByText(/must be exactly 17 digits/i)).toBeInTheDocument();

    // Rerender with 17 digits (valid)
    rerender(
      <StudentIdBar
        studentId="20152691234567890"
        onStudentIdChange={handleChange}
        isValid={true}
        t={messages.en}
      />
    );

    expect(screen.getByText(/✓ Valid \(17 digits\)/i)).toBeInTheDocument();
  });

  it('Maps error codes to friendly human language (Bengali & English)', () => {
    expect(getErrorMessage('NO_SEATS_AVAILABLE', undefined, 'bn')).toBe(
      'এই ক্লাসে আর কোনো সিট খালি নেই। সব সিট সংরক্ষিত।'
    );
    expect(getErrorMessage('NO_SEATS_AVAILABLE', undefined, 'en')).toBe(
      'All seats in this class are currently held or confirmed. No seats available.'
    );
    expect(getErrorMessage('ALREADY_APPLIED', undefined, 'bn')).toBe(
      'আপনি ইতোমধ্যে এই ক্লাসে আবেদন করেছেন অথবা ভর্তি নিশ্চিত করেছেন।'
    );
  });
});
