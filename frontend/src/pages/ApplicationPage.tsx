import React, { useState, useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { api, HoldResponse, ClassItem, ApiError } from '../api/client';
import { Translation, getErrorMessage, Language } from '../i18n/messages';

interface ApplicationPageProps {
  studentId: string;
  t: Translation;
  lang: Language;
}

export const ApplicationPage: React.FC<ApplicationPageProps> = ({ studentId, t, lang }) => {
  const { holdId } = useParams<{ holdId: string }>();
  const navigate = useNavigate();

  const [hold, setHold] = useState<HoldResponse | null>(null);
  const [classInfo, setClassInfo] = useState<ClassItem | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [actionLoading, setActionLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const [remainingSeconds, setRemainingSeconds] = useState<number | null>(null);
  const serverOffsetRef = useRef<number>(0);
  const reconciledRef = useRef<boolean>(false);

  const fetchHoldData = async () => {
    if (!holdId) return;
    try {
      setLoading(true);
      setError(null);
      const data = await api.getHold(holdId);
      setHold(data);

      if (data.serverTime) {
        const serverTime = new Date(data.serverTime).getTime();
        const clientNow = Date.now();
        serverOffsetRef.current = serverTime - clientNow;
      }

      try {
        const cls = await api.getClass(data.classId);
        setClassInfo(cls);
      } catch {
        // non-blocking
      }
    } catch (err: any) {
      if (err instanceof ApiError) {
        setError(getErrorMessage(err.code, err.message, lang));
      } else {
        setError(t.errors.GENERIC_ERROR);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchHoldData();
  }, [holdId]);

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
        setError(getErrorMessage(err.code, err.message, lang));
      } else {
        setError(t.errors.GENERIC_ERROR);
      }
      fetchHoldData();
    } finally {
      setActionLoading(false);
    }
  };

  const handleCancel = async () => {
    if (!hold) return;
    if (!window.confirm(t.confirmCancelPrompt)) return;

    setActionLoading(true);
    setError(null);

    try {
      await api.cancelHold(hold.holdId, studentId);
      setHold((prev) => (prev ? { ...prev, status: 'CANCELLED' } : null));
    } catch (err: any) {
      if (err instanceof ApiError) {
        setError(getErrorMessage(err.code, err.message, lang));
      } else {
        setError(t.errors.GENERIC_ERROR);
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
      <div style={{ maxWidth: '650px', margin: '3rem auto', padding: '0 1rem', textAlign: 'center' }}>
        <p style={{ color: '#64748b' }}>আবেদনের তথ্য লোড হচ্ছে...</p>
      </div>
    );
  }

  if (error && !hold) {
    return (
      <div style={{ maxWidth: '650px', margin: '3rem auto', padding: '0 1rem' }}>
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '1.25rem',
          borderRadius: '8px',
          border: '1px solid #ef4444',
          marginBottom: '1rem'
        }}>
          {error}
        </div>
        <button
          onClick={() => navigate('/')}
          style={{
            padding: '0.65rem 1.4rem',
            backgroundColor: '#0284c7',
            color: '#fff',
            border: 'none',
            borderRadius: '6px',
            cursor: 'pointer',
            fontWeight: 600
          }}
        >
          {t.backToClasses}
        </button>
      </div>
    );
  }

  if (!hold) return null;

  return (
    <div style={{ maxWidth: '700px', margin: '2rem auto', padding: '0 1rem' }}>
      <button
        onClick={() => navigate('/')}
        style={{
          background: 'none',
          border: 'none',
          color: '#0284c7',
          cursor: 'pointer',
          padding: 0,
          marginBottom: '1.25rem',
          fontWeight: 600,
          fontSize: '0.95rem'
        }}
      >
        {t.backToClasses}
      </button>

      <div style={{
        backgroundColor: '#ffffff',
        borderRadius: '12px',
        border: '1px solid #e2e8f0',
        padding: '2.25rem',
        boxShadow: '0 4px 12px rgba(0,0,0,0.05)'
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.5rem' }}>
          <div>
            <h1 style={{ margin: 0, color: '#0f172a', fontSize: '1.5rem' }}>{t.applicationTitle}</h1>
            <p style={{ color: '#64748b', margin: '0.3rem 0 0', fontSize: '0.88rem' }}>
              {t.applicationId}: <code style={{ color: '#0f172a', fontWeight: 700 }}>{hold.holdId}</code>
            </p>
          </div>
          <span style={{
            padding: '0.35rem 0.85rem',
            borderRadius: '999px',
            fontSize: '0.85rem',
            fontWeight: 800,
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

        {error && (
          <div style={{
            backgroundColor: '#fee2e2',
            color: '#b91c1c',
            padding: '0.85rem 1.25rem',
            borderRadius: '8px',
            marginBottom: '1.5rem',
            border: '1px solid #ef4444',
            fontWeight: 500
          }}>
            ❌ {error}
          </div>
        )}

        {/* Status Banners */}
        {hold.status === 'CONFIRMED' && (
          <div style={{
            backgroundColor: '#f0fdf4',
            color: '#15803d',
            padding: '1.25rem',
            borderRadius: '8px',
            marginBottom: '1.5rem',
            border: '1px solid #86efac',
            fontWeight: 600,
            fontSize: '1.05rem',
            display: 'flex',
            alignItems: 'center',
            gap: '0.75rem'
          }}>
            <span style={{ fontSize: '1.5rem' }}>🎉</span>
            {t.confirmedBanner}
          </div>
        )}

        {hold.status === 'EXPIRED' && (
          <div style={{
            backgroundColor: '#fffbeb',
            color: '#b45309',
            padding: '1.25rem',
            borderRadius: '8px',
            marginBottom: '1.5rem',
            border: '1px solid #fcd34d',
            fontWeight: 600,
            fontSize: '1rem'
          }}>
            ⚠️ {t.expiredBanner}
          </div>
        )}

        {hold.status === 'CANCELLED' && (
          <div style={{
            backgroundColor: '#f8fafc',
            color: '#475569',
            padding: '1.25rem',
            borderRadius: '8px',
            marginBottom: '1.5rem',
            border: '1px solid #cbd5e1',
            fontWeight: 600,
            fontSize: '1rem'
          }}>
            ✕ {t.cancelledBanner}
          </div>
        )}

        {/* Information Box */}
        <div style={{
          backgroundColor: '#f8fafc',
          padding: '1.5rem',
          borderRadius: '8px',
          border: '1px solid #f1f5f9',
          marginBottom: '1.5rem'
        }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '1rem' }}>
            <div>
              <span style={{ color: '#64748b', fontSize: '0.88rem', display: 'block' }}>ক্লাস / শিফট:</span>
              <strong style={{ color: '#0f172a', fontSize: '1.1rem' }}>{classInfo?.name || hold.classId}</strong>
            </div>
            <div>
              <span style={{ color: '#64748b', fontSize: '0.88rem', display: 'block' }}>শিক্ষার্থী আইডি:</span>
              <strong style={{ color: '#0f172a', fontSize: '1.05rem', fontFamily: 'monospace' }}>
                {hold.studentId || studentId}
              </strong>
            </div>
          </div>

          {hold.status === 'HELD' && remainingSeconds !== null && (
            <div style={{
              marginTop: '1.5rem',
              paddingTop: '1.25rem',
              borderTop: '1px solid #e2e8f0',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between'
            }}>
              <span style={{ color: '#334155', fontWeight: 600 }}>{t.expiresInLabel}:</span>
              <div style={{
                backgroundColor: remainingSeconds < 60 ? '#fee2e2' : '#e0f2fe',
                color: remainingSeconds < 60 ? '#dc2626' : '#0369a1',
                padding: '0.4rem 1rem',
                borderRadius: '8px',
                fontSize: '1.4rem',
                fontWeight: 800,
                fontFamily: 'monospace'
              }}>
                ⏳ {formatCountdown(remainingSeconds)}
              </div>
            </div>
          )}
        </div>

        {/* Action Buttons */}
        <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
          <button
            onClick={handleConfirm}
            disabled={hold.status !== 'HELD' || actionLoading || remainingSeconds === 0}
            style={{
              flex: '1 1 200px',
              backgroundColor: hold.status === 'HELD' && remainingSeconds !== 0 ? '#16a34a' : '#cbd5e1',
              color: '#ffffff',
              border: 'none',
              padding: '1rem 1.5rem',
              borderRadius: '8px',
              fontWeight: 700,
              fontSize: '1.05rem',
              cursor: hold.status === 'HELD' && remainingSeconds !== 0 && !actionLoading ? 'pointer' : 'not-allowed',
              boxShadow: hold.status === 'HELD' && remainingSeconds !== 0 ? '0 4px 10px rgba(22, 163, 74, 0.3)' : 'none',
              transition: 'background-color 0.2s'
            }}
          >
            {actionLoading ? t.submitting : `💳 ${t.payFeeButton}`}
          </button>

          {hold.status === 'HELD' && (
            <button
              onClick={handleCancel}
              disabled={actionLoading}
              style={{
                backgroundColor: '#ffffff',
                color: '#dc2626',
                border: '2px solid #fca5a5',
                padding: '1rem 1.5rem',
                borderRadius: '8px',
                fontWeight: 700,
                fontSize: '0.95rem',
                cursor: actionLoading ? 'not-allowed' : 'pointer',
                transition: 'border-color 0.2s'
              }}
            >
              {t.cancelApplicationButton}
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
