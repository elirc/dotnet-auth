import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';
import { z } from 'zod';
import { api } from '../api/client';

const schema = z.object({
  tenantSlug: z.string().min(1),
  displayName: z.string().min(2),
  email: z.string().email(),
  password: z.string().min(10)
});

type FormValues = z.infer<typeof schema>;

export function SignupPage() {
  const navigate = useNavigate();
  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { tenantSlug: 'default' } });
  const signup = useMutation({ mutationFn: api.signup, onSuccess: () => navigate('/login') });

  return (
    <main className="auth-screen">
      <section className="auth-panel">
        <h1>Create account</h1>
        <form onSubmit={form.handleSubmit((values) => signup.mutate(values))}>
          <label>Tenant slug<input {...form.register('tenantSlug')} /></label>
          <label>Name<input {...form.register('displayName')} /></label>
          <label>Email<input type="email" {...form.register('email')} /></label>
          <label>Password<input type="password" {...form.register('password')} /></label>
          {signup.error && <p className="error">{signup.error.message}</p>}
          <button disabled={signup.isPending}>{signup.isPending ? 'Creating...' : 'Create account'}</button>
        </form>
        <Link to="/login">Back to login</Link>
      </section>
    </main>
  );
}
