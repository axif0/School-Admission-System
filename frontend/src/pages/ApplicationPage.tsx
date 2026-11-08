import React, { useState, useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { api, HoldResponse, ClassItem, ApiError } from '../api/client';
import { messages, getErrorMessage } from '../i18n/messages';

interface ApplicationPageProps {
  studentId: string;
}

export const ApplicationPage: React.FC<ApplicationPageProps> = ({ studentId }) => {
  const { holdId } = useParams<{ holdId: string }>();
  const navigate = useNavigate();

  const [hold, setHold] = useState<HoldResponse | null>(null);
  const [classInfo, setClassInfo] = useState<ClassItem | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [actionLoading, setActionLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Time remaining in seconds
  const [remainingSeconds, setRemainingSeconds] = useState<number | null>(null);
  const serverOffsetRef = useRef<number>(0); // serverTime - clientNow (ms)
  const reconciledRef = useRef<boolean>(false);

  const fetchHoldData = async () => {
    if (!holdId) return;
    try {
      setLoading(true);
      setError(null);
      const data = await api.getHold(holdId);
      setHold(data);

      // Compute server clock offset
      if (data.serverTime) {
        const serverTime = new Date(data.serverTime).getTime();
        const clientNow = Date.now();
        serverOffsetRef.current = serverTime - clientNow;
      }

      // Fetch class info
      try {
        const cls = await api.getClass(data.classId);
        setClassInfo(cls);
      } catch {
        // non-blocking
      }
    } catch (err: any) {
      if (err instanceof ApiError) {
        setError(getErrorMessage(err.code, err.message));
      } else {
        setError(messages.en.errors.GENERIC_ERROR);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchHoldData();
  }, [holdId]);

  // 1-second countdown tick
  useEffect(() => {
    if (!hold || hold.status !== 'HELD') {
      setRemainingSeconds(null);
      return;
    }

    const updateTimer = () => {
      const expiresAt = new Date(hold.expiresAt).getTime();
      const currentServerTime = Date.now() + serverOffsetRef.current;
      const diffMs = expiresAt - currentServerTime;
      const secondsLeft = Math.max(0, Math.floor(diffMs / 1000));

      setRemainingSeconds(secondsLeft);

      // At zero, immediately flip UI to EXPIRED without reload, and re-fetch once to reconcile
      if (secondsLeft === 0 && hold.status === 'HELD') {
        setHold((prev) => (prev ? { ...prev, status: 'EXPIRED' } : null));

        if (!reconciledRef.current) {
          reconciledRef.current = true;
          api.getHold(hold.holdId).then((fresh) => {
            setHold(fresh);
          }).catch(() => {});
        }
      }
    };

    updateTimer();
    const interval = setInterval(updateTimer, 1000);
    return () => clearInterval(interval);
  }, [hold?.expiresAt, hold?.status]);

  const handleConfirm = async () => {
    if (!hold) return;
    setActionLoading(true);
    setError(null);

    try {
      const result = await api.confirmHold(hold.holdId, studentId);
      setHold((prev) => (prev ? { ...prev, status: result.status } : null));
    } catch (err: any) {
      if (err instanceof ApiError) {
        setError(getErrorMessage(err.code, err.message));
      } else {
        setError(messages.en.errors.GENERIC_ERROR);
      }
      // Re-fetch state
      fetchHoldData();
    } finally {
      setActionLoading(false);
    }
  };

  const handleCancel = async () => {
    if (!hold) return;
    if (!window.confirm(messages.en.confirmCancelPrompt)) return;

    setActionLoading(true);
    setError(null);

    try {
      await api.cancelHold(hold.holdId, studentId);
      setHold((prev) => (prev ? { ...prev, status: 'CANCELLED' } : null));
    } catch (err: any) {
      if (err instanceof ApiError) {
        setError(getErrorMessage(err.code, err.message));
      } else {
        setError(messages.en.errors.GENERIC_ERROR);
      }
      fetchHoldData();
    } finally {
      setActionLoading(false);
    }
  };

  const formatCountdown = (totalSeconds: number) => {
    const mins = Math.floor(totalSeconds / 60);
    const secs = totalSeconds % 60;
    return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  };

  if (loading) {
    return (
      <div style={{ maxWidth: '600px', margin: '3rem auto', padding: '0 1rem' }}>
        <p>Loading application details...</p>
      </div>
    );
  }

  if (error && !hold) {
    return (
      <div style={{ maxWidth: '600px', margin: '3rem auto', padding: '0 1rem' }}>
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '1rem',
          borderRadius: '8px',
          border: '1px solid #ef4444'
        }}>
          {error}
        </div>
        <button
          onClick={() => navigate('/')}
          style={{
            marginTop: '1rem',
            padding: '0.6rem 1.2rem',
            backgroundColor: '#2563eb',
            color: '#fff',
            border: 'none',
            borderRadius: '6px',
            cursor: 'pointer'
          }}
        >
          ← Back to Classes
        </button>
      </div>
    );
  }

  if (!hold) return null;

  return (
    <div style={{ maxWidth: '650px', margin: '2rem auto', padding: '0 1rem' }}>
      <button
        onClick={() => navigate('/')}
        style={{
          background: 'none',
          border: 'none',
          color: '#2563eb',
          cursor: 'pointer',
          padding: 0,
          marginBottom: '1rem',
          fontWeight: 600
        }}
      >
        ← Back to Classes
      </button>

      <div style={{
        backgroundColor: '#fff',
        borderRadius: '8px',
        border: '1px solid #e2e8f0',
        padding: '2rem',
        boxShadow: '0 1px 3px rgba(0,0,0,0.06)'
      }}>
        <h1 style={{ margin: '0 0 0.5rem', color: '#0f172a' }}>{messages.en.applicationTitle}</h1>
        <p style={{ color: '#64748b', margin: '0 0 1.5rem', fontSize: '0.95rem' }}>
          {messages.en.applicationId}: <code style={{ color: '#0f172a', fontWeight: 600 }}>{hold.holdId}</code>
        </p>

        {error && (
          <div style={{
            backgroundColor: '#fee2e2',
            color: '#b91c1c',
            padding: '0.75rem 1rem',
            borderRadius: '6px',
            marginBottom: '1.5rem',
            border: '1px solid #ef4444'
          }}>
            {error}
          </div>
        )}

        {/* Status Banners */}
        {hold.status === 'CONFIRMED' && (
          <div style={{
            backgroundColor: '#dcfce7',
            color: '#15803d',
            padding: '1rem',
            borderRadius: '6px',
            marginBottom: '1.5rem',
            border: '1px solid #22c55e',
            fontWeight: 600
          }}>
            ✓ {messages.en.confirmedBanner}
          </div>
        )}

        {hold.status === 'EXPIRED' && (
          <div style={{
            backgroundColor: '#fef3c7',
            color: '#b45309',
            padding: '1rem',
            borderRadius: '6px',
            marginBottom: '1.5rem',
            border: '1px solid #f59e0b',
            fontWeight: 600
          }}>
            ⚠️ {messages.en.expiredBanner}
          </div>
        )}

        {hold.status === 'CANCELLED' && (
          <div style={{
            backgroundColor: '#f1f5f9',
            color: '#475569',
            padding: '1rem',
            borderRadius: '6px',
            marginBottom: '1.5rem',
            border: '1px solid #cbd5e1',
            fontWeight: 600
          }}>
            ✕ {messages.en.cancelledBanner}
          </div>
        )}

        {/* Details Grid */}
        <div style={{
          backgroundColor: '#f8fafc',
          padding: '1.25rem',
          borderRadius: '6px',
          marginBottom: '1.5rem'
        }}>
          <div style={{ marginBottom: '0.75rem' }}>
            <span style={{ color: '#64748b', fontSize: '0.9rem' }}>Class / Shift: </span>
            <strong style={{ color: '#1e293b' }}>{classInfo?.name || hold.classId}</strong>
          </div>
          <div style={{ marginBottom: '0.75rem' }}>
            <span style={{ color: '#64748b', fontSize: '0.9rem' }}>{messages.en.statusLabel}: </span>
            <span style={{
              display: 'inline-block',
              padding: '0.2rem 0.6rem',
              borderRadius: '4px',
              fontSize: '0.85rem',
              fontWeight: 700,
              backgroundColor:
                hold.status === 'CONFIRMED' ? '#dcfce7' :
                hold.status === 'HELD' ? '#e0f2fe' :
                hold.status === 'EXPIRED' ? '#fef3c7' : '#f1f5f9',
              color:
                hold.status === 'CONFIRMED' ? '#15803d' :
                hold.status === 'HELD' ? '#0369a1' :
                hold.status === 'EXPIRED' ? '#b45309' : '#475569'
            }}>
              {hold.status}
            </span>
          </div>

          {hold.status === 'HELD' && remainingSeconds !== null && (
            <div>
              <span style={{ color: '#64748b', fontSize: '0.9rem' }}>{messages.en.expiresInLabel}: </span>
              <strong style={{
                fontSize: '1.3rem',
                fontFamily: 'monospace',
                color: remainingSeconds < 60 ? '#dc2626' : '#2563eb'
              }}>
                {formatCountdown(remainingSeconds)}
              </strong>
            </div>
          )}
        </div>

        {/* Action Buttons */}
        <div style={{ display: 'flex', gap: '1rem', marginTop: '1.5rem' }}>
          <button
            onClick={handleConfirm}
            disabled={hold.status !== 'HELD' || actionLoading || remainingSeconds === 0}
            style={{
              flex: 1,
              backgroundColor: hold.status === 'HELD' && remainingSeconds !== 0 ? '#16a34a' : '#94a3b8',
              color: '#fff',
              border: 'none',
              padding: '0.85rem',
              borderRadius: '6px',
              fontWeight: 700,
              fontSize: '1rem',
              cursor: hold.status === 'HELD' && remainingSeconds !== 0 && !actionLoading ? 'pointer' : 'not-allowed',
              opacity: actionLoading ? 0.7 : 1
            }}
          >
            {actionLoading ? messages.en.submitting : messages.en.payFeeButton}
          </button>

          {hold.status === 'HELD' && (
            <button
              onClick={handleCancel}
              disabled={actionLoading}
              style={{
                backgroundColor: '#fff',
                color: '#dc2626',
                border: '1px solid #dc2626',
                padding: '0.85rem 1.5rem',
                borderRadius: '6px',
                fontWeight: 600,
                fontSize: '0.95rem',
                cursor: actionLoading ? 'not-allowed' : 'pointer',
                opacity: actionLoading ? 0.7 : 1
              }}
            >
              {messages.en.cancelApplicationButton}
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
