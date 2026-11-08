import React from 'react';
import { Translation } from '../i18n/messages';

interface StudentIdBarProps {
  studentId: string;
  onStudentIdChange: (id: string) => void;
  isValid: boolean;
  t: Translation;
}

export const StudentIdBar: React.FC<StudentIdBarProps> = ({
  studentId,
  onStudentIdChange,
  isValid,
  t
}) => {
  const handleFillDemo = () => {
    // Generate a valid realistic 17-digit birth registration number
    onStudentIdChange('20152691234567890');
  };

  return (
    <div style={{
      backgroundColor: '#ffffff',
      borderBottom: '1px solid #e2e8f0',
      padding: '1.25rem 2rem',
      boxShadow: '0 2px 4px rgba(0,0,0,0.03)'
    }}>
      <div style={{ maxWidth: '1000px', margin: '0 auto' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.4rem' }}>
          <label
            htmlFor="student-id-input"
            style={{
              fontWeight: 700,
              color: '#0f172a',
              fontSize: '0.95rem'
            }}
          >
            {t.studentIdLabel} <span style={{ color: '#ef4444' }}>*</span>
          </label>
          <button
            type="button"
            onClick={handleFillDemo}
            style={{
              background: '#f1f5f9',
              border: '1px solid #cbd5e1',
              borderRadius: '4px',
              padding: '0.2rem 0.6rem',
              fontSize: '0.8rem',
              color: '#334155',
              cursor: 'pointer',
              fontWeight: 500
            }}
          >
            ⚡ {t.demoIdBtn}
          </button>
        </div>

        <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
          <div style={{ position: 'relative', flex: 1 }}>
            <input
              id="student-id-input"
              type="text"
              value={studentId}
              onChange={(e) => onStudentIdChange(e.target.value.trim())}
              placeholder={t.studentIdPlaceholder}
              maxLength={17}
              style={{
                width: '100%',
                padding: '0.75rem 1rem',
                fontSize: '1.05rem',
                border: `2px solid ${!studentId ? '#cbd5e1' : isValid ? '#10b981' : '#f43f5e'}`,
                borderRadius: '8px',
                outline: 'none',
                fontFamily: 'monospace',
                boxSizing: 'border-box',
                backgroundColor: isValid ? '#f0fdf4' : '#ffffff',
                transition: 'border-color 0.2s, background-color 0.2s'
              }}
            />
          </div>

          {studentId && (
            <div style={{
              padding: '0.5rem 0.9rem',
              borderRadius: '6px',
              fontSize: '0.88rem',
              fontWeight: 700,
              backgroundColor: isValid ? '#dcfce7' : '#fee2e2',
              color: isValid ? '#15803d' : '#b91c1c',
              whiteSpace: 'nowrap'
            }}>
              {isValid ? t.studentIdValid : `${studentId.length} / 17`}
            </div>
          )}
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: '0.4rem', fontSize: '0.82rem' }}>
          <span style={{ color: '#64748b' }}>
            ℹ️ {t.studentIdHelp}
          </span>
          {studentId && !isValid && (
            <span style={{ color: '#dc2626', fontWeight: 600 }}>
              {t.studentIdInvalid}
            </span>
          )}
        </div>
      </div>
    </div>
  );
};
