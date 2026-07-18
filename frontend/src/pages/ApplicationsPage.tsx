import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, session } from '../api/client';
import { AsyncState } from '../components/AsyncState';

const schema = z.object({
  tenantId: z.string().uuid(),
  name: z.string().min(2),
  type: z.enum(['Spa', 'WebApp', 'MachineToMachine']),
  redirectUris: z.string(),
  grantTypes: z.string(),
  requireConsent: z.boolean(),
  refreshTokenLifetimeDays: z.coerce.number().min(1).max(365)
});

type FormValues = z.infer<typeof schema>;

export function ApplicationsPage() {
  const [createdSecret, setCreatedSecret] = useState<string | null>(null);
  const tenantId = session.tenantId;
  const queryClient = useQueryClient();
  const apps = useQuery({ queryKey: ['apps', tenantId], queryFn: () => api.applications(tenantId), enabled: !!tenantId });
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      tenantId,
      name: 'NetLinear',
      type: 'WebApp',
      redirectUris: 'http://localhost:5174/callback',
      grantTypes: 'AuthorizationCode,RefreshToken',
      requireConsent: true,
      refreshTokenLifetimeDays: 30
    }
  });
  const create = useMutation({
    mutationFn: (values: FormValues) => api.createApplication({
      ...values,
      redirectUris: values.redirectUris.split(',').map((x) => x.trim()).filter(Boolean),
      grantTypes: values.grantTypes.split(',').map((x) => x.trim()).filter(Boolean)
    }),
    onSuccess: (result) => {
      setCreatedSecret(result.clientSecret ?? 'Public client: no secret generated');
      queryClient.invalidateQueries({ queryKey: ['apps', tenantId] });
    }
  });

  return (
    <section>
      <div className="page-header"><div><p className="eyebrow">OAuth clients</p><h1>Applications</h1></div></div>
      <div className="split">
        <form className="panel" onSubmit={form.handleSubmit((values) => create.mutate(values))}>
          <h2>Register client</h2>
          <label>Tenant ID<input {...form.register('tenantId')} /></label>
          <label>Name<input {...form.register('name')} /></label>
          <label>Type<select {...form.register('type')}><option>Spa</option><option>WebApp</option><option>MachineToMachine</option></select></label>
          <label>Callback URLs<input {...form.register('redirectUris')} /></label>
          <label>Grant types<input {...form.register('grantTypes')} /></label>
          <label className="check"><input type="checkbox" {...form.register('requireConsent')} /> Require consent</label>
          <label>Refresh token days<input type="number" {...form.register('refreshTokenLifetimeDays')} /></label>
          {create.error && <p className="error">{create.error.message}</p>}
          {createdSecret && <p className="notice">Client secret: <code>{createdSecret}</code></p>}
          <button disabled={create.isPending}>{create.isPending ? 'Creating...' : 'Create application'}</button>
        </form>
        <AsyncState loading={apps.isLoading} error={apps.error} empty={!apps.data?.length}>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Name</th><th>Client ID</th><th>Type</th><th>Grants</th></tr></thead>
              <tbody>{apps.data?.map((app) => <tr key={app.id}><td>{app.name}</td><td><code>{app.clientId}</code></td><td>{app.type}</td><td>{app.grantTypes.join(', ')}</td></tr>)}</tbody>
            </table>
          </div>
        </AsyncState>
      </div>
    </section>
  );
}
