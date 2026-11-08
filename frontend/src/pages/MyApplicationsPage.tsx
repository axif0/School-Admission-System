import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api, MyHoldItem, ApiError } from '../api/client';
import { messages, getErrorMessage } from '../i18n/messages';

interface MyApplicationsPageProps {
  studentId: string;
  isStudentIdValid: boolean;
}

export const MyApplicationsPage: React.FC<MyApplicationsPageProps> = ({
  studentId,
  isStudentIdValid
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
        setError(getErrorMessage(err.code, err.message));
      } else {
        setError(messages.en.errors.GENERIC_ERROR);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchMyHolds();
  }, [studentId, isStudentIdValid]);

  return (
    <div style={{ maxWidth: '900px', margin: '2rem auto', padding: '0 1rem' }}>
      <h1 style={{ color: '#0f172a', marginBottom: '1.5rem' }}>{messages.en.myApplicationsTitle}</h1>

      {!isStudentIdValid ? (
        <div style={{
          backgroundColor: '#eff6ff',
          padding: '2rem',
          borderRadius: '8px',
          border: '1px solid #bfdbfe',
          textAlign: 'center'
        }}>
          <p style={{ color: '#1e40af', fontSize: '1.05rem', margin: 0 }}>
            Please enter your 17-digit birth registration number in the top bar to view your applications.
          </p>
        </div>
      ) : error ? (
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '0.75rem 1rem',
          borderRadius: '6px',
          border: '1px solid #ef4444'
        }}>
          {error}
        </div>
      ) : loading ? (
        <p>Loading your applications...</p>
      ) : holds.length === 0 ? (
        <div style={{
          backgroundColor: '#f8fafc',
          padding: '3rem',
          borderRadius: '8px',
          border: '1px dashed #cbd5e1',
          textAlign: 'center'
        }}>
          <p style={{ color: '#64748b', fontSize: '1.1rem', marginBottom: '1rem' }}>
            {messages.en.noApplications}
          </p>
          <Link
            to="/"
            style={{
              color: '#2563eb',
              fontWeight: 600,
              textDecoration: 'none'
            }}
          >
            Browse available classes to apply →
          </Link>
        </div>
      ) : (
        <div style={{ display: 'grid', gap: '1rem' }}>
          {holds.map((h) => (
            <div
              key={h.holdId}
              style={{
                backgroundColor: '#fff',
                padding: '1.5rem',
                borderRadius: '8px',
                border: '1px solid #e2e8f0',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                boxShadow: '0 1px 2px rgba(0,0,0,0.04)'
              }}
            >
              <div>
                <h3 style={{ margin: '0 0 0.5rem', color: '#1e293b' }}>{h.className}</h3>
                <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
                  <span style={{
                    padding: '0.2rem 0.6rem',
                    borderRadius: '4px',
                    fontSize: '0.85rem',
                    fontWeight: 700,
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
                  <span style={{ color: '#64748b', fontSize: '0.85rem' }}>
                    ID: {h.holdId}
                  </span>
                </div>
              </div>

              <Link
                to={`/holds/${h.holdId}`}
                style={{
                  backgroundColor: '#2563eb',
                  color: '#fff',
                  padding: '0.55rem 1.2rem',
                  borderRadius: '6px',
                  textDecoration: 'none',
                  fontWeight: 600,
                  fontSize: '0.9rem'
                }}
              >
                {messages.en.viewDetails} →
              </Link>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
