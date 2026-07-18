import { Navigate, Route, Routes } from 'react-router-dom';
import { Shell } from '../components/Shell';
import { ApplicationsPage } from '../pages/ApplicationsPage';
import { AuditPage } from '../pages/AuditPage';
import { CallbackPage } from '../pages/CallbackPage';
import { DashboardPage } from '../pages/DashboardPage';
import { LoginPage } from '../pages/LoginPage';
import { SignupPage } from '../pages/SignupPage';
import { UsersPage } from '../pages/UsersPage';

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/signup" element={<SignupPage />} />
      <Route path="/callback" element={<CallbackPage />} />
      <Route element={<Shell />}>
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/applications" element={<ApplicationsPage />} />
        <Route path="/users" element={<UsersPage />} />
        <Route path="/audit" element={<AuditPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
