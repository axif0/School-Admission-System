import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { Translation, getErrorMessage, Language } from '../i18n/messages';

interface CreateClassPageProps {
  t: Translation;
  lang: Language;
}

export const CreateClassPage: React.FC<CreateClassPageProps> = ({ t, lang }) => {
  const [name, setName] = useState('');
  const [totalSeats, setTotalSeats] = useState<number | ''>(120);
  const [adminKey, setAdminKey] = useState<string>(() => {
    return typeof window !== 'undefined' ? (sessionStorage.getItem('admin_api_key') || '') : '';
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setError(lang === 'bn' ? 'অনুগ্রহ করে ক্লাসের নাম দিন।' : 'Please enter class name.');
      return;
    }
    if (typeof totalSeats !== 'number' || totalSeats <= 0) {
      setError(lang === 'bn' ? 'মোট সিট সংখ্যা ১ বা তার বেশি হতে হবে।' : 'Total seats must be 1 or more.');
      return;
    }

    setLoading(true);
    setError(null);
    setSuccess(null);

    try {
      await api.createClass({
        name: name.trim(),
        totalSeats
      }, adminKey.trim() || null);
      setSuccess(t.classCreatedSuccess);
      setTimeout(() => navigate('/'), 1200);
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

  return (
    <div style={{ maxWidth: '650px', margin: '2rem auto', padding: '0 1rem' }}>
      <h1 style={{ color: '#0f172a', marginBottom: '1.5rem', fontSize: '1.65rem' }}>{t.createClassTitle}</h1>

      {error && (
        <div style={{
          backgroundColor: '#fee2e2',
          color: '#b91c1c',
          padding: '0.85rem 1.25rem',
          borderRadius: '8px',
          marginBottom: '1rem',
          border: '1px solid #ef4444'
        }}>
          ❌ {error}
        </div>
      )}

      {success && (
        <div style={{
          backgroundColor: '#dcfce7',
          color: '#15803d',
          padding: '0.85rem 1.25rem',
          borderRadius: '8px',
          marginBottom: '1rem',
          border: '1px solid #22c55e',
          fontWeight: 600
        }}>
          ✓ {success}
        </div>
      )}

      <form onSubmit={handleSubmit} style={{
        backgroundColor: '#ffffff',
        padding: '2.25rem',
        borderRadius: '12px',
        border: '1px solid #e2e8f0',
        boxShadow: '0 4px 12px rgba(0,0,0,0.04)'
      }}>
        <div style={{ marginBottom: '1.5rem' }}>
          <label
            htmlFor="class-name"
            style={{ display: 'block', fontWeight: 700, marginBottom: '0.5rem', color: '#1e293b' }}
          >
            {t.className} <span style={{ color: '#ef4444' }}>*</span>
          </label>
          <input
            id="class-name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t.classNamePlaceholder}
            required
            maxLength={100}
            style={{
              width: '100%',
              padding: '0.8rem 1rem',
              borderRadius: '8px',
              border: '1px solid #cbd5e1',
              fontSize: '1rem',
              boxSizing: 'border-box'
            }}
          />
        </div>

        <div style={{ marginBottom: '1.75rem' }}>
          <label
            htmlFor="total-seats"
            style={{ display: 'block', fontWeight: 700, marginBottom: '0.5rem', color: '#1e293b' }}
          >
            {t.totalSeats} <span style={{ color: '#ef4444' }}>*</span>
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
              padding: '0.8rem 1rem',
              borderRadius: '8px',
              border: '1px solid #cbd5e1',
              fontSize: '1rem',
              boxSizing: 'border-box'
            }}
          />
        </div>

        <div style={{ marginBottom: '1.75rem' }}>
          <label
            htmlFor="admin-key"
            style={{ display: 'block', fontWeight: 700, marginBottom: '0.5rem', color: '#1e293b' }}
          >
            {lang === 'bn' ? 'অ্যাডমিন কী (যদি সার্ভারে প্রয়োজন হয়)' : 'Admin Key (optional / if configured)'}
          </label>
          <input
            id="admin-key"
            type="password"
            value={adminKey}
            onChange={(e) => {
              const val = e.target.value;
              setAdminKey(val);
              if (typeof window !== 'undefined') {
                if (val) {
                  sessionStorage.setItem('admin_api_key', val);
                } else {
                  sessionStorage.removeItem('admin_api_key');
                }
              }
            }}
            placeholder={lang === 'bn' ? 'প্রয়োজন হলে অ্যাডমিন কী দিন' : 'Enter admin secret key if required'}
            style={{
              width: '100%',
              padding: '0.8rem 1rem',
              borderRadius: '8px',
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
              backgroundColor: '#0284c7',
              color: '#ffffff',
              border: 'none',
              padding: '0.85rem 1.75rem',
              borderRadius: '8px',
              fontWeight: 700,
              fontSize: '1rem',
              cursor: loading ? 'not-allowed' : 'pointer',
              boxShadow: '0 4px 10px rgba(2, 132, 199, 0.25)'
            }}
          >
            {loading ? t.submitting : 'সংরক্ষণ করুন (Save Class)'}
          </button>
          <button
            type="button"
            onClick={() => navigate('/')}
            style={{
              backgroundColor: '#f1f5f9',
              color: '#475569',
              border: '1px solid #cbd5e1',
              padding: '0.85rem 1.5rem',
              borderRadius: '8px',
              fontWeight: 600,
              fontSize: '1rem',
              cursor: 'pointer'
            }}
          >
            বাতিল
          </button>
        </div>
      </form>
    </div>
  );
};
