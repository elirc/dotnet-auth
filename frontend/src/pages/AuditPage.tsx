import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api, session } from '../api/client';
import { AsyncState } from '../components/AsyncState';

export function AuditPage() {
  const tenantId = session.tenantId;
  const [eventType, setEventType] = useState('');
  const audit = useQuery({ queryKey: ['audit', tenantId, eventType], queryFn: () => api.audit(tenantId, eventType), enabled: !!tenantId });

  return (
    <section>
      <div className="page-header">
        <div><p className="eyebrow">Immutable trail</p><h1>Audit explorer</h1></div>
        <input placeholder="Filter event type" value={eventType} onChange={(e) => setEventType(e.target.value)} />
      </div>
      <AsyncState loading={audit.isLoading} error={audit.error} empty={!audit.data?.items.length}>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Event</th><th>Actor</th><th>IP</th><th>Time</th><th>Data</th></tr></thead>
            <tbody>
              {audit.data?.items.map((event) => (
                <tr key={event.id}>
                  <td>{event.eventType}</td>
                  <td>{event.actorUserId ?? event.actorClientId ?? 'system'}</td>
                  <td>{event.ipAddress ?? '-'}</td>
                  <td>{new Date(event.occurredAt).toLocaleString()}</td>
                  <td><code>{event.dataJson}</code></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </AsyncState>
    </section>
  );
}
