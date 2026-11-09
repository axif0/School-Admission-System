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
    expect(getErrorMessage('ADMIN_KEY_REQUIRED', undefined, 'en')).toBe(
      'Class creation requires an admin key. Please provide the admin key.'
    );
    expect(getErrorMessage('ADMIN_KEY_INVALID', undefined, 'en')).toBe(
      'The provided admin key is invalid.'
    );
  });

  it('Sends X-Admin-Key header when admin key is provided or stored in sessionStorage', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      status: 201,
      ok: true,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ id: 'c_test1', name: 'Test Class', totalSeats: 50 })
    });
    vi.stubGlobal('fetch', fetchMock);

    const { api } = await import('../api/client');

    // 1. Explicit admin key parameter
    await api.createClass({ name: 'Admin Class 1', totalSeats: 40 }, 'secret-admin-pass');
    expect(fetchMock).toHaveBeenCalled();
    const lastCall = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
    const headers = lastCall[1].headers as Headers;
    expect(headers.get('X-Admin-Key')).toBe('secret-admin-pass');

    // 2. From sessionStorage
    sessionStorage.setItem('admin_api_key', 'session-token-123');
    await api.createClass({ name: 'Admin Class 2', totalSeats: 40 });
    const sessionCall = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
    const sessionHeaders = sessionCall[1].headers as Headers;
    expect(sessionHeaders.get('X-Admin-Key')).toBe('session-token-123');
    sessionStorage.removeItem('admin_api_key');
  });
});
