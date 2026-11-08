import React from 'react';
import { messages } from '../i18n/messages';

interface StudentIdBarProps {
  studentId: string;
  onStudentIdChange: (id: string) => void;
  isValid: boolean;
}

export const StudentIdBar: React.FC<StudentIdBarProps> = ({
  studentId,
  onStudentIdChange,
  isValid
}) => {
  const t = messages.en;

  return (
    <div style={{
      backgroundColor: '#f8fafc',
      borderBottom: '2px solid #e2e8f0',
      padding: '1rem 2rem',
      boxShadow: '0 1px 3px rgba(0,0,0,0.05)'
    }}>
      <div style={{ maxWidth: '1000px', margin: '0 auto' }}>
        <label
          htmlFor="student-id-input"
          style={{
            display: 'block',
            fontWeight: 600,
            marginBottom: '0.4rem',
            color: '#1e293b',
            fontSize: '0.95rem'
          }}
        >
          {t.studentIdLabel} <span style={{ color: '#ef4444' }}>*</span>
        </label>
        <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
          <input
            id="student-id-input"
            type="text"
            value={studentId}
            onChange={(e) => onStudentIdChange(e.target.value.trim())}
            placeholder={t.studentIdPlaceholder}
            maxLength={17}
            style={{
              flex: 1,
              padding: '0.6rem 0.9rem',
              fontSize: '1rem',
              border: `2px solid ${!studentId ? '#cbd5e1' : isValid ? '#10b981' : '#ef4444'}`,
              borderRadius: '6px',
              outline: 'none',
              fontFamily: 'monospace'
            }}
          />
          {studentId && (
            <span style={{
              fontSize: '0.85rem',
              fontWeight: 600,
              color: isValid ? '#059669' : '#dc2626',
              whiteSpace: 'nowrap'
            }}>
              {isValid ? '✓ Valid (17 digits)' : `${studentId.length}/17 digits`}
            </span>
          )}
        </div>
        {studentId && !isValid && (
          <p style={{ color: '#dc2626', margin: '0.4rem 0 0', fontSize: '0.85rem' }}>
            {t.studentIdInvalid}
          </p>
        )}
      </div>
    </div>
  );
};
