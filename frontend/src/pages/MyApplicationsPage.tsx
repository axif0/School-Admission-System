import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api, MyHoldItem, ApiError } from '../api/client';
import { Translation, getErrorMessage, Language } from '../i18n/messages';

interface MyApplicationsPageProps {
  studentId: string;
  isStudentIdValid: boolean;
  t: Translation;
  lang: Language;
}

export const MyApplicationsPage: React.FC<MyApplicationsPageProps> = ({
  studentId,
  isStudentIdValid,
  t,
  lang
}) => {
  const [holds, setHolds] = useState<MyHoldItem[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const fetchMyHolds = async () => {
    if (!isStudentIdValid || !studentId) {
      setHolds([]);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      setError(null);
      const data = await api.getMyHolds(studentId);
      setHolds(data);
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
    fetchMyHolds();
  }, [studentId, isStudentIdValid]);

  return (
    <div style={{ maxWidth: '950px', margin: '2rem auto', padding: '0 1rem' }}>
      <h1 style={{ color: '#0f172a', marginBottom: '1.5rem', fontSize: '1.65rem' }}>{t.myApplicationsTitle}</h1>

      {!isStudentIdValid ? (
        <div style={{
          backgroundColor: '#eff6ff',
          padding: '2.5rem',
          borderRadius: '12px',
          border: '1px solid #bfdbfe',
          textAlign: 'center'
        }}>
          <p style={{ color: '#1e40af', fontSize: '1.1rem', margin: 0 }}>
            {t.enterIdPrompt}
          </p>
        </div>
      ) : error ? (
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '0.85rem 1.25rem',
          borderRadius: '8px',
          border: '1px solid #ef4444'
        }}>
          ❌ {error}
        </div>
      ) : loading ? (
        <p style={{ color: '#64748b' }}>আবেদনের তালিকা লোড হচ্ছে...</p>
      ) : holds.length === 0 ? (
        <div style={{
          backgroundColor: '#ffffff',
          padding: '3.5rem',
          borderRadius: '12px',
          border: '2px dashed #cbd5e1',
          textAlign: 'center'
        }}>
          <p style={{ color: '#64748b', fontSize: '1.15rem', marginBottom: '1.25rem' }}>
            {t.noApplications}
          </p>
          <Link
            to="/"
            style={{
              color: '#0284c7',
              fontWeight: 700,
              textDecoration: 'none',
              fontSize: '1rem'
            }}
          >
            {t.browseClasses}
          </Link>
        </div>
      ) : (
        <div style={{ display: 'grid', gap: '1.25rem' }}>
          {holds.map((h) => (
            <div
              key={h.holdId}
              style={{
                backgroundColor: '#ffffff',
                padding: '1.5rem 1.75rem',
                borderRadius: '12px',
                border: '1px solid #e2e8f0',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                boxShadow: '0 2px 6px rgba(0,0,0,0.03)',
                flexWrap: 'wrap',
                gap: '1rem'
              }}
            >
              <div>
                <h3 style={{ margin: '0 0 0.5rem', color: '#0f172a', fontSize: '1.2rem' }}>{h.className}</h3>
                <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', flexWrap: 'wrap' }}>
                  <span style={{
                    padding: '0.25rem 0.75rem',
                    borderRadius: '999px',
                    fontSize: '0.85rem',
                    fontWeight: 800,
                    backgroundColor:
                      h.status === 'CONFIRMED' ? '#dcfce7' :
                      h.status === 'HELD' ? '#e0f2fe' :
                      h.status === 'EXPIRED' ? '#fef3c7' : '#f1f5f9',
                    color:
                      h.status === 'CONFIRMED' ? '#15803d' :
                      h.status === 'HELD' ? '#0369a1' :
                      h.status === 'EXPIRED' ? '#b45309' : '#475569'
                  }}>
                    {h.status}
                  </span>
                  <span style={{ color: '#64748b', fontSize: '0.88rem' }}>
                    আইডি: <code style={{ color: '#0f172a' }}>{h.holdId}</code>
                  </span>
                </div>
              </div>

              <Link
                to={`/holds/${h.holdId}`}
                style={{
                  backgroundColor: '#0284c7',
                  color: '#ffffff',
                  padding: '0.65rem 1.4rem',
                  borderRadius: '8px',
                  textDecoration: 'none',
                  fontWeight: 700,
                  fontSize: '0.92rem',
                  boxShadow: '0 2px 4px rgba(2, 132, 199, 0.2)'
                }}
              >
                {t.viewDetails} →
              </Link>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
