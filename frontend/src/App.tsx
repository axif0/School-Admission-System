import React, { useState } from 'react';
import { Routes, Route, Link } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { StudentIdBar } from './components/StudentIdBar';
import { ClassListPage } from './pages/ClassListPage';
import { CreateClassPage } from './pages/CreateClassPage';
import { ApplicationPage } from './pages/ApplicationPage';
import { MyApplicationsPage } from './pages/MyApplicationsPage';
import { messages } from './i18n/messages';

const STORAGE_KEY = 'school_admission_student_id';

export const App: React.FC = () => {
  const [studentId, setStudentId] = useState<string>(() => {
    return localStorage.getItem(STORAGE_KEY) || '';
  });

  const isStudentIdValid = /^\d{17}$/.test(studentId);

  const handleStudentIdChange = (newId: string) => {
    setStudentId(newId);
    if (newId) {
      localStorage.setItem(STORAGE_KEY, newId);
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  };

  return (
    <ErrorBoundary>
      <div style={{ minHeight: '100vh', backgroundColor: '#f1f5f9', color: '#1e293b' }}>
        {/* Navigation Bar */}
        <header style={{
          backgroundColor: '#0f172a',
          color: '#fff',
          padding: '1rem 2rem'
        }}>
          <div style={{
            maxWidth: '1000px',
            margin: '0 auto',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center'
          }}>
            <Link to="/" style={{ color: '#fff', textDecoration: 'none', fontSize: '1.25rem', fontWeight: 700 }}>
              🏫 {messages.en.appTitle}
            </Link>
            <nav style={{ display: 'flex', gap: '1.5rem' }}>
              <Link to="/" style={{ color: '#cbd5e1', textDecoration: 'none', fontWeight: 500 }}>
                {messages.en.navClasses}
              </Link>
              <Link to="/my-applications" style={{ color: '#cbd5e1', textDecoration: 'none', fontWeight: 500 }}>
                {messages.en.navMyApplications}
              </Link>
              <Link to="/classes/new" style={{ color: '#93c5fd', textDecoration: 'none', fontWeight: 500 }}>
                {messages.en.navCreateClass}
              </Link>
            </nav>
          </div>
        </header>

        {/* 17-digit Student ID Bar */}
        <StudentIdBar
          studentId={studentId}
          onStudentIdChange={handleStudentIdChange}
          isValid={isStudentIdValid}
        />

        {/* Main Content Area */}
        <main>
          <Routes>
            <Route
              path="/"
              element={<ClassListPage studentId={studentId} isStudentIdValid={isStudentIdValid} />}
            />
            <Route path="/classes/new" element={<CreateClassPage />} />
            <Route
              path="/holds/:holdId"
              element={<ApplicationPage studentId={studentId} />}
            />
            <Route
              path="/my-applications"
              element={<MyApplicationsPage studentId={studentId} isStudentIdValid={isStudentIdValid} />}
            />
          </Routes>
        </main>
      </div>
    </ErrorBoundary>
  );
};
