export interface ClassItem {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface CreateClassPayload {
  name: string;
  totalSeats: number;
}

export interface HoldResponse {
  holdId: string;
  classId: string;
  studentId?: string;
  status: 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED';
  expiresAt: string;
  serverTime?: string;
}

export interface ConfirmResponse {
  holdId: string;
  status: 'CONFIRMED';
}

export interface MyHoldItem {
  holdId: string;
  classId: string;
  className: string;
  status: 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED';
  expiresAt: string;
}

export interface ApiErrorResponse {
  error: {
    code: string;
    message: string;
  };
}

export class ApiError extends Error {
  public code: string;
  public status: number;

  constructor(code: string, message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
  }
}

declare global {
  interface Window {
    __APP_CONFIG__?: {
      API_URL?: string;
    };
  }
}

export function getBaseUrl(): string {
  if (typeof window !== 'undefined' && window.__APP_CONFIG__?.API_URL) {
    return window.__APP_CONFIG__.API_URL.replace(/\/$/, '');
  }
  return ((import.meta as any).env?.VITE_API_URL as string || 'http://localhost:8080').replace(/\/$/, '');
}

async function request<T>(
  endpoint: string,
  options: RequestInit = {},
  studentId?: string | null
): Promise<T> {
  const url = `${getBaseUrl()}${endpoint}`;
  const headers = new Headers(options.headers || {});

  if (!headers.has('Content-Type') && options.body) {
    headers.set('Content-Type', 'application/json');
  }

  if (studentId) {
    headers.set('X-Student-Id', studentId);
  }

  try {
    const res = await fetch(url, { ...options, headers });

    if (res.status === 204) {
      return undefined as unknown as T;
    }

    const contentType = res.headers.get('content-type');
    const isJson = contentType && contentType.includes('application/json');
    const data = isJson ? await res.json() : await res.text();

    if (!res.ok) {
      if (isJson && data?.error) {
        throw new ApiError(data.error.code, data.error.message, res.status);
      }
      throw new ApiError('GENERIC_ERROR', typeof data === 'string' ? data : 'Request failed', res.status);
    }

    return data as T;
  } catch (err: any) {
    if (err instanceof ApiError) {
      throw err;
    }
    throw new ApiError('NETWORK_ERROR', err?.message || 'Network request failed', 0);
  }
}

export const api = {
  getClasses: () => request<ClassItem[]>('/classes'),
  getClass: (classId: string) => request<ClassItem>(`/classes/${classId}`),
  createClass: (payload: CreateClassPayload) =>
    request<ClassItem>('/classes', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  createHold: (classId: string, studentId: string) =>
    request<HoldResponse>(`/classes/${classId}/holds`, { method: 'POST' }, studentId),
  getHold: (holdId: string) => request<HoldResponse>(`/holds/${holdId}`),
  confirmHold: (holdId: string, studentId: string) =>
    request<ConfirmResponse>(`/holds/${holdId}/confirm`, { method: 'POST' }, studentId),
  cancelHold: (holdId: string, studentId: string) =>
    request<void>(`/holds/${holdId}`, { method: 'DELETE' }, studentId),
  getMyHolds: (studentId: string) => request<MyHoldItem[]>('/me/holds', { method: 'GET' }, studentId)
};
