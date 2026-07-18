import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

export function CallbackPage() {
  const [params] = useSearchParams();
  const [message, setMessage] = useState('Processing callback...');

  useEffect(() => {
    const code = params.get('code');
    const state = params.get('state');
    if (!code) {
      setMessage('No authorization code was returned.');
      return;
    }

    setMessage(`Authorization code received for state ${state ?? 'none'}. Exchange it with the stored PKCE verifier at /connect/token.`);
  }, [params]);

  return (
    <main className="auth-screen">
      <section className="auth-panel">
        <h1>OIDC callback</h1>
        <p>{message}</p>
        <Link to="/dashboard">Open dashboard</Link>
      </section>
    </main>
  );
}
