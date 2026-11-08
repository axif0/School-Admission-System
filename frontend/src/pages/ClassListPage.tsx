import React, { useState, useEffect, useRef } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ClassItem, MyHoldItem, ApiError } from '../api/client';
import { messages, getErrorMessage } from '../i18n/messages';

interface ClassListPageProps {
  studentId: string;
  isStudentIdValid: boolean;
}

export const ClassListPage: React.FC<ClassListPageProps> = ({
  studentId,
  isStudentIdValid
}) => {
  const [classes, setClasses] = useState<ClassItem[]>([]);
  const [myHolds, setMyHolds] = useState<MyHoldItem[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [applyingClassId, setApplyingClassId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [networkWarning, setNetworkWarning] = useState<boolean>(false);
  
  const navigate = useNavigate();
  const isPollingRef = useRef<boolean>(false);

  // Fetch classes and active holds
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
          // ignore student holds failure in background
        }
      } else {
        setMyHolds([]);
      }
    } catch (err: any) {
      if (err instanceof ApiError && err.code === 'NETWORK_ERROR') {
        setNetworkWarning(true);
      } else if (!isBackground) {
        setErrorMessage(getErrorMessage(err?.code, err?.message));
      }
    } finally {
      if (!isBackground) setLoading(false);
      isPollingRef.current = false;
    }
  };

  useEffect(() => {
    fetchData();

    // 4-second polling, paused when tab is hidden
    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') {
        fetchData(true);
      }
    }, 4000);

    return () => clearInterval(interval);
  }, [studentId, isStudentIdValid]);

  const handleApply = async (classId: string) => {
    if (!isStudentIdValid) {
      setErrorMessage(messages.en.errors.INVALID_STUDENT_ID);
      return;
    }

    setApplyingClassId(classId);
    setErrorMessage(null);

    try {
      const hold = await api.createHold(classId, studentId);
      navigate(`/holds/${hold.holdId}`);
    } catch (err: any) {
      setErrorMessage(getErrorMessage(err?.code, err?.message));
      // Re-fetch to update seat numbers
      fetchData();
    } finally {
      setApplyingClassId(null);
    }
  };

  // Check if student has active or confirmed application in this class
  const getExistingHold = (classId: string) => {
    return myHolds.find(
      (h) => h.classId === classId && (h.status === 'HELD' || h.status === 'CONFIRMED')
    );
  };

  return (
    <div style={{ maxWidth: '1000px', margin: '2rem auto', padding: '0 1rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.5rem' }}>
        <h1 style={{ margin: 0, color: '#0f172a' }}>{messages.en.classListTitle}</h1>
        <Link
          to="/classes/new"
          style={{
            backgroundColor: '#2563eb',
            color: '#fff',
            padding: '0.6rem 1.2rem',
            borderRadius: '6px',
            textDecoration: 'none',
            fontWeight: 600
          }}
        >
          + {messages.en.navCreateClass}
        </Link>
      </div>

      {networkWarning && (
        <div style={{
          backgroundColor: '#fef3c7',
          color: '#92400e',
          padding: '0.75rem 1rem',
          borderRadius: '6px',
          marginBottom: '1rem',
          border: '1px solid #f59e0b'
        }}>
          ⚠️ {messages.en.errors.NETWORK_ERROR}
        </div>
      )}

      {errorMessage && (
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '0.75rem 1rem',
          borderRadius: '6px',
          marginBottom: '1rem',
          border: '1px solid #ef4444'
        }}>
          {errorMessage}
        </div>
      )}

      {loading ? (
        <p>Loading available classes...</p>
      ) : classes.length === 0 ? (
        <div style={{
          padding: '3rem',
          textAlign: 'center',
          backgroundColor: '#f8fafc',
          borderRadius: '8px',
          border: '1px dashed #cbd5e1'
        }}>
          <p style={{ color: '#64748b', fontSize: '1.1rem' }}>No classes or shifts have been created yet.</p>
          <Link
            to="/classes/new"
            style={{
              color: '#2563eb',
              fontWeight: 600
            }}
          >
            Create the first class shift →
          </Link>
        </div>
      ) : (
        <div style={{ display: 'grid', gap: '1rem' }}>
          {classes.map((cls) => {
            const existingHold = getExistingHold(cls.id);
            const isFull = cls.availableSeats <= 0;
            const isApplying = applyingClassId === cls.id;

            return (
              <div
                key={cls.id}
                style={{
                  backgroundColor: '#fff',
                  border: '1px solid #e2e8f0',
                  borderRadius: '8px',
                  padding: '1.5rem',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  boxShadow: '0 1px 2px rgba(0,0,0,0.04)'
                }}
              >
                <div>
                  <h3 style={{ margin: '0 0 0.5rem', color: '#1e293b' }}>{cls.name}</h3>
                  <div style={{ display: 'flex', gap: '1.5rem', fontSize: '0.9rem' }}>
                    <span style={{ color: '#475569' }}>
                      <strong>{cls.totalSeats}</strong> Total
                    </span>
                    <span style={{ color: '#d97706' }}>
                      <strong>{cls.heldSeats}</strong> Held
                    </span>
                    <span style={{ color: '#059669' }}>
                      <strong>{cls.confirmedSeats}</strong> Confirmed
                    </span>
                    <span style={{
                      color: isFull ? '#dc2626' : '#2563eb',
                      fontWeight: 700
                    }}>
                      <strong>{cls.availableSeats}</strong> Available
                    </span>
                  </div>
                </div>

                <div>
                  {existingHold ? (
                    <Link
                      to={`/holds/${existingHold.holdId}`}
                      style={{
                        backgroundColor: '#f1f5f9',
                        color: '#0284c7',
                        border: '1px solid #0284c7',
                        padding: '0.6rem 1.2rem',
                        borderRadius: '6px',
                        textDecoration: 'none',
                        fontWeight: 600,
                        display: 'inline-block'
                      }}
                    >
                      {messages.en.viewApplicationButton} ({existingHold.status})
                    </Link>
                  ) : (
                    <button
                      onClick={() => handleApply(cls.id)}
                      disabled={isFull || !isStudentIdValid || isApplying}
                      style={{
                        backgroundColor: isFull || !isStudentIdValid ? '#94a3b8' : '#16a34a',
                        color: '#fff',
                        border: 'none',
                        padding: '0.6rem 1.4rem',
                        borderRadius: '6px',
                        fontWeight: 600,
                        cursor: isFull || !isStudentIdValid || isApplying ? 'not-allowed' : 'pointer',
                        opacity: isApplying ? 0.7 : 1
                      }}
                    >
                      {isApplying ? messages.en.submitting : messages.en.applyButton}
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
