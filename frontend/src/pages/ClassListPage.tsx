import React, { useState, useEffect, useRef } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ClassItem, MyHoldItem, ApiError } from '../api/client';
import { Translation, getErrorMessage, Language } from '../i18n/messages';

interface ClassListPageProps {
  studentId: string;
  isStudentIdValid: boolean;
  t: Translation;
  lang: Language;
}

export const ClassListPage: React.FC<ClassListPageProps> = ({
  studentId,
  isStudentIdValid,
  t,
  lang
}) => {
  const [classes, setClasses] = useState<ClassItem[]>([]);
  const [myHolds, setMyHolds] = useState<MyHoldItem[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [applyingClassId, setApplyingClassId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [networkWarning, setNetworkWarning] = useState<boolean>(false);

  const navigate = useNavigate();
  const isPollingRef = useRef<boolean>(false);

  const fetchData = async (isBackground = false) => {
    if (isPollingRef.current && isBackground) return;
    if (isBackground) isPollingRef.current = true;

    try {
      const classesData = await api.getClasses();
      setClasses(classesData);
      setNetworkWarning(false);

      if (isStudentIdValid && studentId) {
        try {
          const holds = await api.getMyHolds(studentId);
          setMyHolds(holds);
        } catch {
          // ignore
        }
      } else {
        setMyHolds([]);
      }
    } catch (err: any) {
      if (err instanceof ApiError && err.code === 'NETWORK_ERROR') {
        setNetworkWarning(true);
      } else if (!isBackground) {
        setErrorMessage(getErrorMessage(err?.code, err?.message, lang));
      }
    } finally {
      if (!isBackground) setLoading(false);
      isPollingRef.current = false;
    }
  };

  useEffect(() => {
    fetchData();

    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') {
        fetchData(true);
      }
    }, 4000);

    return () => clearInterval(interval);
  }, [studentId, isStudentIdValid]);

  const handleApply = async (classId: string) => {
    if (!isStudentIdValid) {
      setErrorMessage(t.errors.INVALID_STUDENT_ID);
      return;
    }

    setApplyingClassId(classId);
    setErrorMessage(null);

    try {
      const hold = await api.createHold(classId, studentId);
      navigate(`/holds/${hold.holdId}`);
    } catch (err: any) {
      setErrorMessage(getErrorMessage(err?.code, err?.message, lang));
      fetchData();
    } finally {
      setApplyingClassId(null);
    }
  };

  const getExistingHold = (classId: string) => {
    return myHolds.find(
      (h) => h.classId === classId && (h.status === 'HELD' || h.status === 'CONFIRMED')
    );
  };

  return (
    <div style={{ maxWidth: '1000px', margin: '2rem auto', padding: '0 1rem' }}>
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        marginBottom: '1.5rem',
        flexWrap: 'wrap',
        gap: '1rem'
      }}>
        <div>
          <h1 style={{ margin: 0, color: '#0f172a', fontSize: '1.65rem' }}>{t.classListTitle}</h1>
          <p style={{ margin: '0.3rem 0 0', color: '#64748b', fontSize: '0.9rem' }}>
            {t.seatsStatus} (স্বয়ংক্রিয়ভাবে প্রতি ৪ সেকেন্ডে আপডেট হয়)
          </p>
        </div>
        <Link
          to="/classes/new"
          style={{
            backgroundColor: '#0284c7',
            color: '#fff',
            padding: '0.65rem 1.25rem',
            borderRadius: '8px',
            textDecoration: 'none',
            fontWeight: 600,
            fontSize: '0.92rem',
            boxShadow: '0 2px 4px rgba(2, 132, 199, 0.25)'
          }}
        >
          + {t.navCreateClass}
        </Link>
      </div>

      {networkWarning && (
        <div style={{
          backgroundColor: '#fef3c7',
          color: '#92400e',
          padding: '0.75rem 1rem',
          borderRadius: '8px',
          marginBottom: '1rem',
          border: '1px solid #f59e0b',
          fontSize: '0.9rem'
        }}>
          ⚠️ {t.errors.NETWORK_ERROR}
        </div>
      )}

      {errorMessage && (
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '0.75rem 1.25rem',
          borderRadius: '8px',
          marginBottom: '1.25rem',
          border: '1px solid #ef4444',
          fontWeight: 500
        }}>
          ❌ {errorMessage}
        </div>
      )}

      {loading ? (
        <div style={{ textAlign: 'center', padding: '3rem', color: '#64748b' }}>
          <p style={{ fontSize: '1.1rem' }}>ক্লাস তালিকা লোড হচ্ছে...</p>
        </div>
      ) : classes.length === 0 ? (
        <div style={{
          padding: '3.5rem',
          textAlign: 'center',
          backgroundColor: '#ffffff',
          borderRadius: '12px',
          border: '2px dashed #cbd5e1'
        }}>
          <p style={{ color: '#475569', fontSize: '1.15rem', marginBottom: '1rem' }}>{t.noClassesYet}</p>
          <Link
            to="/classes/new"
            style={{
              color: '#0284c7',
              fontWeight: 700,
              textDecoration: 'none',
              fontSize: '1rem'
            }}
          >
            {t.createFirstClass}
          </Link>
        </div>
      ) : (
        <div style={{ display: 'grid', gap: '1.25rem' }}>
          {classes.map((cls) => {
            const existingHold = getExistingHold(cls.id);
            const isFull = cls.availableSeats <= 0;
            const isApplying = applyingClassId === cls.id;
            const filledPercentage = Math.round(((cls.totalSeats - cls.availableSeats) / cls.totalSeats) * 100);

            return (
              <div
                key={cls.id}
                style={{
                  backgroundColor: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: '12px',
                  padding: '1.5rem',
                  boxShadow: '0 2px 8px rgba(0,0,0,0.04)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  flexWrap: 'wrap',
                  gap: '1.25rem',
                  transition: 'transform 0.15s, box-shadow 0.15s'
                }}
              >
                <div style={{ flex: '1 1 300px' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '0.75rem' }}>
                    <h3 style={{ margin: 0, color: '#0f172a', fontSize: '1.2rem' }}>{cls.name}</h3>
                    {isFull && (
                      <span style={{
                        backgroundColor: '#fee2e2',
                        color: '#b91c1c',
                        padding: '0.15rem 0.5rem',
                        borderRadius: '4px',
                        fontSize: '0.75rem',
                        fontWeight: 700
                      }}>
                        {t.capacityFull}
                      </span>
                    )}
                  </div>

                  {/* Seat Counts */}
                  <div style={{
                    display: 'flex',
                    gap: '1.25rem',
                    fontSize: '0.92rem',
                    marginBottom: '0.85rem',
                    flexWrap: 'wrap'
                  }}>
                    <span style={{ color: '#475569' }}>
                      {t.totalSeats}: <strong style={{ color: '#0f172a' }}>{cls.totalSeats}</strong>
                    </span>
                    <span style={{ color: '#d97706' }}>
                      {t.heldSeats}: <strong>{cls.heldSeats}</strong>
                    </span>
                    <span style={{ color: '#16a34a' }}>
                      {t.confirmedSeats}: <strong>{cls.confirmedSeats}</strong>
                    </span>
                    <span style={{
                      color: isFull ? '#dc2626' : '#0284c7',
                      fontWeight: 700
                    }}>
                      {t.availableSeats}: <strong>{cls.availableSeats}</strong>
                    </span>
                  </div>

                  {/* Seat Progress Bar */}
                  <div style={{
                    width: '100%',
                    maxWidth: '340px',
                    height: '8px',
                    backgroundColor: '#e2e8f0',
                    borderRadius: '4px',
                    overflow: 'hidden'
                  }}>
                    <div style={{
                      width: `${Math.min(100, filledPercentage)}%`,
                      height: '100%',
                      backgroundColor: isFull ? '#ef4444' : filledPercentage > 75 ? '#f59e0b' : '#10b981',
                      transition: 'width 0.4s ease'
                    }} />
                  </div>
                </div>

                <div>
                  {existingHold ? (
                    <Link
                      to={`/holds/${existingHold.holdId}`}
                      style={{
                        backgroundColor: existingHold.status === 'CONFIRMED' ? '#dcfce7' : '#e0f2fe',
                        color: existingHold.status === 'CONFIRMED' ? '#15803d' : '#0369a1',
                        border: `1px solid ${existingHold.status === 'CONFIRMED' ? '#86efac' : '#7dd3fc'}`,
                        padding: '0.7rem 1.4rem',
                        borderRadius: '8px',
                        textDecoration: 'none',
                        fontWeight: 700,
                        fontSize: '0.95rem',
                        display: 'inline-block'
                      }}
                    >
                      {t.viewApplicationButton} ({existingHold.status})
                    </Link>
                  ) : (
                    <button
                      onClick={() => handleApply(cls.id)}
                      disabled={isFull || !isStudentIdValid || isApplying}
                      title={!isStudentIdValid ? t.studentIdInvalid : undefined}
                      style={{
                        backgroundColor: isFull || !isStudentIdValid ? '#cbd5e1' : '#16a34a',
                        color: isFull || !isStudentIdValid ? '#64748b' : '#ffffff',
                        border: 'none',
                        padding: '0.75rem 1.6rem',
                        borderRadius: '8px',
                        fontWeight: 700,
                        fontSize: '1rem',
                        cursor: isFull || !isStudentIdValid || isApplying ? 'not-allowed' : 'pointer',
                        boxShadow: isFull || !isStudentIdValid ? 'none' : '0 2px 6px rgba(22, 163, 74, 0.25)',
                        transition: 'background-color 0.2s'
                      }}
                    >
                      {isApplying ? t.submitting : t.applyButton}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
