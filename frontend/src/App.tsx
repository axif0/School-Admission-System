import React, { useState } from 'react';
import { Routes, Route, Link } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { StudentIdBar } from './components/StudentIdBar';
import { ClassListPage } from './pages/ClassListPage';
import { CreateClassPage } from './pages/CreateClassPage';
import { ApplicationPage } from './pages/ApplicationPage';
import { MyApplicationsPage } from './pages/MyApplicationsPage';
import { messages, Language } from './i18n/messages';

const STORAGE_KEY = 'school_admission_student_id';
const LANG_STORAGE_KEY = 'school_admission_lang';

export const App: React.FC = () => {
  const [lang, setLang] = useState<Language>(() => {
    return (localStorage.getItem(LANG_STORAGE_KEY) as Language) || 'bn';
  });

  const [studentId, setStudentId] = useState<string>(() => {
    return localStorage.getItem(STORAGE_KEY) || '';
  });

  const t = messages[lang];
  const isStudentIdValid = /^\d{17}$/.test(studentId);

  const handleStudentIdChange = (newId: string) => {
    setStudentId(newId);
    if (newId) {
      localStorage.setItem(STORAGE_KEY, newId);
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  };

  const toggleLanguage = () => {
    const nextLang: Language = lang === 'bn' ? 'en' : 'bn';
    setLang(nextLang);
    localStorage.setItem(LANG_STORAGE_KEY, nextLang);
  };

  return (
    <ErrorBoundary>
      <div style={{ minHeight: '100vh', backgroundColor: '#f8fafc', color: '#1e293b', fontFamily: 'system-ui, -apple-system, sans-serif' }}>
        {/* Navigation Bar */}
        <header style={{
          backgroundColor: '#0f172a',
          color: '#ffffff',
          padding: '1rem 2rem',
          boxShadow: '0 2px 8px rgba(0,0,0,0.1)'
        }}>
          <div style={{
            maxWidth: '1000px',
            margin: '0 auto',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: '1rem'
          }}>
            <Link to="/" style={{ color: '#ffffff', textDecoration: 'none', fontSize: '1.3rem', fontWeight: 800, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <span>🏫</span> {t.appTitle}
            </Link>

            <div style={{ display: 'flex', alignItems: 'center', gap: '1.5rem', flexWrap: 'wrap' }}>
              <nav style={{ display: 'flex', gap: '1.25rem' }}>
                <Link to="/" style={{ color: '#cbd5e1', textDecoration: 'none', fontWeight: 600, fontSize: '0.95rem' }}>
                  {t.navClasses}
                </Link>
                <Link to="/my-applications" style={{ color: '#cbd5e1', textDecoration: 'none', fontWeight: 600, fontSize: '0.95rem' }}>
                  {t.navMyApplications}
                </Link>
                <Link to="/classes/new" style={{ color: '#38bdf8', textDecoration: 'none', fontWeight: 600, fontSize: '0.95rem' }}>
                  {t.navCreateClass}
                </Link>
              </nav>

              {/* Language Switcher */}
              <button
                type="button"
                onClick={toggleLanguage}
                style={{
                  background: '#1e293b',
                  color: '#f8fafc',
                  border: '1px solid #475569',
                  borderRadius: '20px',
                  padding: '0.35rem 0.85rem',
                  fontSize: '0.82rem',
                  fontWeight: 700,
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.3rem'
                }}
              >
                🌐 {lang === 'bn' ? 'English' : 'বাংলা'}
              </button>
            </div>
          </div>
        </header>

        {/* 17-digit Student ID Bar */}
        <StudentIdBar
          studentId={studentId}
          onStudentIdChange={handleStudentIdChange}
          isValid={isStudentIdValid}
          t={t}
        />

        {/* Main Content Area */}
        <main style={{ paddingBottom: '3rem' }}>
          <Routes>
            <Route
              path="/"
              element={
                <ClassListPage
                  studentId={studentId}
                  isStudentIdValid={isStudentIdValid}
                  t={t}
                  lang={lang}
                />
              }
            />
            <Route
              path="/classes/new"
              element={<CreateClassPage t={t} lang={lang} />}
            />
            <Route
              path="/holds/:holdId"
              element={
                <ApplicationPage
                  studentId={studentId}
                  t={t}
                  lang={lang}
                />
              }
            />
            <Route
              path="/my-applications"
              element={
                <MyApplicationsPage
                  studentId={studentId}
                  isStudentIdValid={isStudentIdValid}
                  t={t}
                  lang={lang}
                />
              }
            />
          </Routes>
        </main>
      </div>
    </ErrorBoundary>
  );
};
