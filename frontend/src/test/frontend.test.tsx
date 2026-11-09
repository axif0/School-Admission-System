import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { StudentIdBar } from '../components/StudentIdBar';
import { ErrorBoundary } from '../components/ErrorBoundary';
import { ApplicationPage } from '../pages/ApplicationPage';
import { ClassListPage } from '../pages/ClassListPage';
import { CreateClassPage } from '../pages/CreateClassPage';
import { MyApplicationsPage } from '../pages/MyApplicationsPage';
import { getErrorMessage, messages } from '../i18n/messages';
import { api, ApiError } from '../api/client';

describe('Frontend Comprehensive Unit & Component Test Suite', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // ---- 1. StudentIdBar & Input Validation ----
  it('1. Validates 17-digit student id and renders error/valid states', () => {
    const handleChange = vi.fn();

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

  it('2. Trims whitespace on StudentIdBar input change and supports Fill Demo ID button', () => {
    const handleChange = vi.fn();
    render(
      <StudentIdBar
        studentId=""
        onStudentIdChange={handleChange}
        isValid={false}
        t={messages.en}
      />
    );

    const input = screen.getByLabelText(/Student Birth Registration Number/i);
    fireEvent.change(input, { target: { value: '  20152691234567890  ' } });
    expect(handleChange).toHaveBeenCalledWith('20152691234567890');

    const demoBtn = screen.getByRole('button', { name: /Fill Demo ID/i });
    fireEvent.click(demoBtn);
    expect(handleChange).toHaveBeenCalledWith('20152691234567890');
  });

  // ---- 2. i18n & Error Localization ----
  it('3. Maps error codes to friendly human language (Bengali & English)', () => {
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
    expect(getErrorMessage('HOLD_EXPIRED', undefined, 'en')).toBe(
      'This seat hold has expired. The seat is no longer reserved.'
    );
    expect(getErrorMessage('TEMPORARILY_BUSY', undefined, 'en')).toBe(
      'The system is currently handling high traffic. Please try again in a few moments.'
    );
  });

  // ---- 3. API Client Headers & Error Parsing ----
  it('4. Sends X-Admin-Key header when admin key is provided or stored in sessionStorage', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      status: 201,
      ok: true,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ id: 'c_test1', name: 'Test Class', totalSeats: 50 })
    });
    vi.stubGlobal('fetch', fetchMock);

    await api.createClass({ name: 'Admin Class 1', totalSeats: 40 }, 'secret-admin-pass');
    const lastCall = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
    const headers = lastCall[1].headers as Headers;
    expect(headers.get('X-Admin-Key')).toBe('secret-admin-pass');

    sessionStorage.setItem('admin_api_key', 'session-token-123');
    await api.createClass({ name: 'Admin Class 2', totalSeats: 40 });
    const sessionCall = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
    const sessionHeaders = sessionCall[1].headers as Headers;
    expect(sessionHeaders.get('X-Admin-Key')).toBe('session-token-123');
    sessionStorage.removeItem('admin_api_key');
  });

  it('5. Sends X-Student-Id header on student endpoints and parses ApiError JSON bodies', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      status: 409,
      ok: false,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({
        error: { code: 'NO_SEATS_AVAILABLE', message: 'Class is full' }
      })
    });
    vi.stubGlobal('fetch', fetchMock);

    await expect(api.createHold('c_1', '20150000000000001')).rejects.toMatchObject({
      code: 'NO_SEATS_AVAILABLE',
      status: 409
    });

    const callHeaders = fetchMock.mock.calls[0][1].headers as Headers;
    expect(callHeaders.get('X-Student-Id')).toBe('20150000000000001');
  });

  // ---- 4. ApplicationPage: Countdown Timer & Clock Skew Compensation ----
  it('6. Renders live hold countdown compensated for server clock skew', async () => {
    // Simulate client clock being 60 seconds behind server clock:
    // Server says serverTime is 12:01:00 and expiresAt is 12:03:00 (exactly 120s = 02:00 remaining).
    const baseTime = new Date('2026-11-09T12:00:00Z').getTime();
    vi.spyOn(Date, 'now').mockReturnValue(baseTime);

    vi.spyOn(api, 'getHold').mockResolvedValue({
      holdId: 'h_skew_1',
      classId: 'c_1',
      studentId: '20150000000000001',
      status: 'HELD',
      serverTime: '2026-11-09T12:01:00Z',
      expiresAt: '2026-11-09T12:03:00Z'
    });
    vi.spyOn(api, 'getClass').mockResolvedValue({
      id: 'c_1',
      name: 'Class 6 - Morning',
      totalSeats: 30,
      heldSeats: 1,
      confirmedSeats: 0,
      availableSeats: 29
    });

    render(
      <MemoryRouter initialEntries={['/holds/h_skew_1']}>
        <Routes>
          <Route
            path="/holds/:holdId"
            element={<ApplicationPage studentId="20150000000000001" t={messages.en} lang="en" />}
          />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText(/02:00/)).toBeInTheDocument();
    });
    expect(screen.getByText('Class 6 - Morning')).toBeInTheDocument();
  });

  it('7. Transitions HELD to EXPIRED when countdown reaches 00:00 and reconciles with server', async () => {
    const nowMs = new Date('2026-11-09T12:00:00Z').getTime();
    vi.spyOn(Date, 'now').mockReturnValue(nowMs);

    const getHoldSpy = vi.spyOn(api, 'getHold')
      .mockResolvedValueOnce({
        holdId: 'h_exp_1',
        classId: 'c_1',
        studentId: '20150000000000001',
        status: 'HELD',
        serverTime: '2026-11-09T12:00:00Z',
        expiresAt: '2026-11-09T12:00:00Z'
      })
      .mockResolvedValueOnce({
        holdId: 'h_exp_1',
        classId: 'c_1',
        studentId: '20150000000000001',
        status: 'EXPIRED',
        serverTime: '2026-11-09T12:00:01Z',
        expiresAt: '2026-11-09T12:00:00Z'
      });
    vi.spyOn(api, 'getClass').mockResolvedValue({
      id: 'c_1',
      name: 'Class 6',
      totalSeats: 10,
      heldSeats: 0,
      confirmedSeats: 0,
      availableSeats: 10
    });

    render(
      <MemoryRouter initialEntries={['/holds/h_exp_1']}>
        <Routes>
          <Route
            path="/holds/:holdId"
            element={<ApplicationPage studentId="20150000000000001" t={messages.en} lang="en" />}
          />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('EXPIRED')).toBeInTheDocument();
    });

    const confirmBtn = screen.getByRole('button', { name: /Pay Admission Fee/i });
    expect(confirmBtn).toBeDisabled();
    expect(getHoldSpy).toHaveBeenCalledTimes(2);
  });

  it('8. Confirms an active hold and transitions UI state to CONFIRMED', async () => {
    const future = new Date(Date.now() + 300_000).toISOString();
    vi.spyOn(api, 'getHold').mockResolvedValue({
      holdId: 'h_conf_1',
      classId: 'c_1',
      studentId: '20150000000000001',
      status: 'HELD',
      expiresAt: future,
      serverTime: new Date().toISOString()
    });
    vi.spyOn(api, 'getClass').mockResolvedValue({
      id: 'c_1',
      name: 'Class 7',
      totalSeats: 20,
      heldSeats: 1,
      confirmedSeats: 0,
      availableSeats: 19
    });
    const confirmSpy = vi.spyOn(api, 'confirmHold').mockResolvedValue({
      holdId: 'h_conf_1',
      status: 'CONFIRMED'
    });

    render(
      <MemoryRouter initialEntries={['/holds/h_conf_1']}>
        <Routes>
          <Route
            path="/holds/:holdId"
            element={<ApplicationPage studentId="20150000000000001" t={messages.en} lang="en" />}
          />
        </Routes>
      </MemoryRouter>
    );

    const confirmBtn = await screen.findByRole('button', { name: /Pay Admission Fee/i });
    expect(confirmBtn).not.toBeDisabled();

    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(confirmSpy).toHaveBeenCalledWith('h_conf_1', '20150000000000001');
      expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
      expect(screen.getByText(/Admission Confirmed! Your seat has been secured/i)).toBeInTheDocument();
    });
  });

  it('9. Cancels an active hold when user confirms prompt and transitions to CANCELLED', async () => {
    const future = new Date(Date.now() + 300_000).toISOString();
    vi.spyOn(api, 'getHold').mockResolvedValue({
      holdId: 'h_canc_1',
      classId: 'c_1',
      studentId: '20150000000000001',
      status: 'HELD',
      expiresAt: future,
      serverTime: new Date().toISOString()
    });
    vi.spyOn(api, 'getClass').mockResolvedValue({
      id: 'c_1',
      name: 'Class 8',
      totalSeats: 20,
      heldSeats: 1,
      confirmedSeats: 0,
      availableSeats: 19
    });
    const cancelSpy = vi.spyOn(api, 'cancelHold').mockResolvedValue(undefined);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    render(
      <MemoryRouter initialEntries={['/holds/h_canc_1']}>
        <Routes>
          <Route
            path="/holds/:holdId"
            element={<ApplicationPage studentId="20150000000000001" t={messages.en} lang="en" />}
          />
        </Routes>
      </MemoryRouter>
    );

    const cancelBtn = await screen.findByRole('button', { name: /Cancel Application/i });
    fireEvent.click(cancelBtn);

    await waitFor(() => {
      expect(cancelSpy).toHaveBeenCalledWith('h_canc_1', '20150000000000001');
      expect(screen.getByText('CANCELLED')).toBeInTheDocument();
    });
  });

  // ---- 5. ClassListPage: Seat Rendering, Full State & Apply Flow ----
  it('10. Renders class list with seat counts and disables apply button when class is full', async () => {
    vi.spyOn(api, 'getClasses').mockResolvedValue([
      { id: 'c_open', name: 'Open Class', totalSeats: 50, heldSeats: 5, confirmedSeats: 10, availableSeats: 35 },
      { id: 'c_full', name: 'Full Class', totalSeats: 20, heldSeats: 10, confirmedSeats: 10, availableSeats: 0 }
    ]);
    vi.spyOn(api, 'getMyHolds').mockResolvedValue([]);

    render(
      <MemoryRouter>
        <ClassListPage
          studentId="20150000000000001"
          isStudentIdValid={true}
          t={messages.en}
          lang="en"
        />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Open Class')).toBeInTheDocument();
      expect(screen.getByText('Full Class')).toBeInTheDocument();
    });

    const applyButtons = screen.getAllByRole('button', { name: /Apply for Seat/i });
    expect(applyButtons).toHaveLength(2);
    expect(applyButtons[0]).not.toBeDisabled(); // Open Class
    expect(applyButtons[1]).toBeDisabled();     // Full Class
  });

  it('11. Applies for a seat on ClassListPage and navigates to /holds/:holdId', async () => {
    vi.spyOn(api, 'getClasses').mockResolvedValue([
      { id: 'c_apply', name: 'Science Shift', totalSeats: 40, heldSeats: 0, confirmedSeats: 0, availableSeats: 40 }
    ]);
    vi.spyOn(api, 'getMyHolds').mockResolvedValue([]);
    const createHoldSpy = vi.spyOn(api, 'createHold').mockResolvedValue({
      holdId: 'h_new_99',
      classId: 'c_apply',
      studentId: '20150000000000001',
      status: 'HELD',
      expiresAt: new Date(Date.now() + 600_000).toISOString(),
      serverTime: new Date().toISOString()
    });

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route
            path="/"
            element={
              <ClassListPage
                studentId="20150000000000001"
                isStudentIdValid={true}
                t={messages.en}
                lang="en"
              />
            }
          />
          <Route path="/holds/:holdId" element={<div>Navigated to Hold Page</div>} />
        </Routes>
      </MemoryRouter>
    );

    const applyBtn = await screen.findByRole('button', { name: /Apply for Seat/i });
    fireEvent.click(applyBtn);

    await waitFor(() => {
      expect(createHoldSpy).toHaveBeenCalledWith('c_apply', '20150000000000001');
      expect(screen.getByText('Navigated to Hold Page')).toBeInTheDocument();
    });
  });

  it('12. Displays network warning banner on ClassListPage when API throws NETWORK_ERROR', async () => {
    vi.spyOn(api, 'getClasses').mockRejectedValue(new ApiError('NETWORK_ERROR', 'Failed to fetch', 0));

    render(
      <MemoryRouter>
        <ClassListPage
          studentId="20150000000000001"
          isStudentIdValid={true}
          t={messages.en}
          lang="en"
        />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText(/Unable to reach the server/i)).toBeInTheDocument();
    });
  });

  // ---- 6. CreateClassPage & MyApplicationsPage & ErrorBoundary ----
  it('13. Submits CreateClassPage form with class name, totalSeats, and adminKey', async () => {
    const createClassSpy = vi.spyOn(api, 'createClass').mockResolvedValue({
      id: 'c_created',
      name: 'Grade 9 - Section A',
      totalSeats: 60,
      heldSeats: 0,
      confirmedSeats: 0,
      availableSeats: 60
    });

    render(
      <MemoryRouter>
        <CreateClassPage t={messages.en} lang="en" />
      </MemoryRouter>
    );

    fireEvent.change(screen.getByLabelText(/Class \/ Shift Name/i), {
      target: { value: 'Grade 9 - Section A' }
    });
    fireEvent.change(screen.getByLabelText(/Total Seats/i), {
      target: { value: '60' }
    });
    fireEvent.change(screen.getByLabelText(/Admin Key/i), {
      target: { value: 'my-admin-secret' }
    });

    fireEvent.click(screen.getByRole('button', { name: /Save Class/i }));

    await waitFor(() => {
      expect(createClassSpy).toHaveBeenCalledWith(
        { name: 'Grade 9 - Section A', totalSeats: 60 },
        'my-admin-secret'
      );
      expect(screen.getByText(/Class created successfully!/i)).toBeInTheDocument();
    });
  });

  it('14. Renders student holds on MyApplicationsPage and prompts when studentId is invalid', async () => {
    vi.spyOn(api, 'getMyHolds').mockResolvedValue([
      {
        holdId: 'h_my_1',
        classId: 'c_1',
        className: 'Class 10 Science',
        status: 'CONFIRMED',
        expiresAt: new Date().toISOString()
      }
    ]);

    const { rerender } = render(
      <MemoryRouter>
        <MyApplicationsPage
          studentId="123"
          isStudentIdValid={false}
          t={messages.en}
          lang="en"
        />
      </MemoryRouter>
    );

    expect(screen.getByText(/Please enter your 17-digit birth registration number in the top bar/i)).toBeInTheDocument();

    rerender(
      <MemoryRouter>
        <MyApplicationsPage
          studentId="20150000000000001"
          isStudentIdValid={true}
          t={messages.en}
          lang="en"
        />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Class 10 Science')).toBeInTheDocument();
      expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
    });
  });

  it('15. Catches child rendering exceptions in ErrorBoundary and displays fallback UI', () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const BrokenComponent = () => {
      throw new Error('Simulated UI crash');
    };

    render(
      <ErrorBoundary>
        <BrokenComponent />
      </ErrorBoundary>
    );

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Reload Page/i })).toBeInTheDocument();
    consoleSpy.mockRestore();
  });
});
