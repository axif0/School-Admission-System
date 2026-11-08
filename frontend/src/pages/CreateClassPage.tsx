import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { messages, getErrorMessage } from '../i18n/messages';

export const CreateClassPage: React.FC = () => {
  const [name, setName] = useState('');
  const [totalSeats, setTotalSeats] = useState<number | ''>(100);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setError('Please provide a class name.');
      return;
    }
    if (typeof totalSeats !== 'number' || totalSeats <= 0) {
      setError('Total seats must be a positive number.');
      return;
    }

    setLoading(true);
    setError(null);
    setSuccess(null);

    try {
      await api.createClass({
        name: name.trim(),
        totalSeats
      });
      setSuccess(messages.en.classCreatedSuccess);
      setTimeout(() => navigate('/'), 1200);
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

  return (
    <div style={{ maxWidth: '600px', margin: '2rem auto', padding: '0 1rem' }}>
      <h1 style={{ color: '#0f172a', marginBottom: '1.5rem' }}>{messages.en.createClassTitle}</h1>

      {error && (
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '0.75rem 1rem',
          borderRadius: '6px',
          marginBottom: '1rem',
          border: '1px solid #ef4444'
        }}>
          {error}
        </div>
      )}

      {success && (
        <div style={{
          backgroundColor: '#dcfce7',
          color: '#15803d',
          padding: '0.75rem 1rem',
          borderRadius: '6px',
          marginBottom: '1rem',
          border: '1px solid #22c55e'
        }}>
          ✓ {success}
        </div>
      )}

      <form onSubmit={handleSubmit} style={{
        backgroundColor: '#fff',
        padding: '2rem',
        borderRadius: '8px',
        border: '1px solid #e2e8f0',
        boxShadow: '0 1px 3px rgba(0,0,0,0.05)'
      }}>
        <div style={{ marginBottom: '1.5rem' }}>
          <label
            htmlFor="class-name"
            style={{ display: 'block', fontWeight: 600, marginBottom: '0.5rem', color: '#334155' }}
          >
            {messages.en.className}
          </label>
          <input
            id="class-name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={messages.en.classNamePlaceholder}
            required
            maxLength={100}
            style={{
              width: '100%',
              padding: '0.7rem',
              borderRadius: '6px',
              border: '1px solid #cbd5e1',
              fontSize: '1rem',
              boxSizing: 'border-box'
            }}
          />
        </div>

        <div style={{ marginBottom: '1.5rem' }}>
          <label
            htmlFor="total-seats"
            style={{ display: 'block', fontWeight: 600, marginBottom: '0.5rem', color: '#334155' }}
          >
            {messages.en.totalSeats}
          </label>
          <input
            id="total-seats"
            type="number"
            min={1}
            max={100000}
            value={totalSeats}
            onChange={(e) => setTotalSeats(e.target.value === '' ? '' : parseInt(e.target.value, 10))}
            required
            style={{
              width: '100%',
              padding: '0.7rem',
              borderRadius: '6px',
              border: '1px solid #cbd5e1',
              fontSize: '1rem',
              boxSizing: 'border-box'
            }}
          />
        </div>

        <div style={{ display: 'flex', gap: '1rem' }}>
          <button
            type="submit"
            disabled={loading}
            style={{
              backgroundColor: '#2563eb',
              color: '#fff',
              border: 'none',
              padding: '0.75rem 1.5rem',
              borderRadius: '6px',
              fontWeight: 600,
              fontSize: '1rem',
              cursor: loading ? 'not-allowed' : 'pointer',
              opacity: loading ? 0.7 : 1
            }}
          >
            {loading ? messages.en.submitting : 'Save & Publish'}
          </button>
          <button
            type="button"
            onClick={() => navigate('/')}
            style={{
              backgroundColor: '#f1f5f9',
              color: '#475569',
              border: '1px solid #cbd5e1',
              padding: '0.75rem 1.5rem',
              borderRadius: '6px',
              fontWeight: 600,
              fontSize: '1rem',
              cursor: 'pointer'
            }}
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
};
