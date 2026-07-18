import { useQuery } from '@tanstack/react-query';
import { api, session } from '../api/client';
import { AsyncState } from '../components/AsyncState';

export function DashboardPage() {
  const tenantId = session.tenantId;
  const apps = useQuery({ queryKey: ['apps', tenantId], queryFn: () => api.applications(tenantId), enabled: !!tenantId });
  const users = useQuery({ queryKey: ['users', tenantId, ''], queryFn: () => api.users(tenantId, ''), enabled: !!tenantId });
  const audit = useQuery({ queryKey: ['audit', tenantId, ''], queryFn: () => api.audit(tenantId, ''), enabled: !!tenantId });

  return (
    <section>
      <div className="page-header">
        <div>
          <p className="eyebrow">Identity provider</p>
          <h1>Security overview</h1>
        </div>
        <input className="tenant-input" placeholder="Tenant ID" defaultValue={tenantId} onBlur={(e) => { session.tenantId = e.target.value; location.reload(); }} />
      </div>
      {!tenantId && <div className="state">Paste the seeded tenant ID to load dashboard data.</div>}
      <div className="metric-grid">
        <Metric label="Applications" value={apps.data?.length ?? 0} />
        <Metric label="Users" value={users.data?.total ?? 0} />
        <Metric label="Audit events" value={audit.data?.total ?? 0} />
      </div>
      <AsyncState loading={audit.isLoading} error={audit.error} empty={!audit.data?.items.length}>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Recent event</th><th>Actor</th><th>Time</th></tr></thead>
            <tbody>
              {audit.data?.items.slice(0, 8).map((event) => (
                <tr key={event.id}>
                  <td>{event.eventType}</td>
                  <td>{event.actorUserId ?? event.actorClientId ?? 'system'}</td>
                  <td>{new Date(event.occurredAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </AsyncState>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return <div className="metric"><span>{label}</span><strong>{value}</strong></div>;
}
