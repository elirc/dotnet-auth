import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';
import { z } from 'zod';
import { api } from '../api/client';

const schema = z.object({
  tenantSlug: z.string().min(1),
  email: z.string().email(),
  password: z.string().min(8),
  totpCode: z.string().optional()
});

type FormValues = z.infer<typeof schema>;

export function LoginPage() {
  const navigate = useNavigate();
  const [mfaPrompt, setMfaPrompt] = useState(false);
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { tenantSlug: 'default', email: 'admin@netauth0.local', password: 'ChangeMe123!', totpCode: '' }
  });
  const login = useMutation({
    mutationFn: api.login,
    onSuccess: (result) => {
      if (result.requiresMfa && !result.accessToken) {
        setMfaPrompt(true);
        return;
      }
      if (result.accessToken) {
        localStorage.setItem('netauth0.access_token', result.accessToken);
      }
      localStorage.setItem('netauth0.tenant_id', result.tenantId);
      navigate('/dashboard');
    }
  });

  return (
    <main className="auth-screen">
      <section className="auth-panel">
        <div className="auth-brand"><KeyRound /> NetAuth0</div>
        <h1>Hosted login</h1>
        <form onSubmit={form.handleSubmit((values) => login.mutate(values))}>
          <label>Tenant slug<input {...form.register('tenantSlug')} /></label>
          <label>Email<input type="email" {...form.register('email')} /></label>
          <label>Password<input type="password" {...form.register('password')} /></label>
          <label>TOTP code<input inputMode="numeric" {...form.register('totpCode')} /></label>
          {mfaPrompt && <p className="notice">Enter the current authenticator code to finish signing in.</p>}
          {login.error && <p className="error">{login.error.message}</p>}
          <button disabled={login.isPending}>{login.isPending ? 'Signing in...' : 'Sign in'}</button>
        </form>
        <Link to="/signup">Create an account</Link>
      </section>
    </main>
  );
}
