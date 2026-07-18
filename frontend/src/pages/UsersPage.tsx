import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, session } from '../api/client';
import { AsyncState } from '../components/AsyncState';

export function UsersPage() {
  const tenantId = session.tenantId;
  const [query, setQuery] = useState('');
  const queryClient = useQueryClient();
  const users = useQuery({ queryKey: ['users', tenantId, query], queryFn: () => api.users(tenantId, query), enabled: !!tenantId });
  const block = useMutation({
    mutationFn: ({ userId, blocked }: { userId: string; blocked: boolean }) => api.blockUser(tenantId, userId, blocked),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['users', tenantId] })
  });
  const logout = useMutation({ mutationFn: (userId: string) => api.forceLogout(tenantId, userId) });

  return (
    <section>
      <div className="page-header">
        <div><p className="eyebrow">Directory</p><h1>Users</h1></div>
        <input placeholder="Search users" value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>
      <AsyncState loading={users.isLoading} error={users.error} empty={!users.data?.items.length}>
        <div className="table-wrap">
          <table>
            <thead><tr><th>User</th><th>Status</th><th>Created</th><th>Actions</th></tr></thead>
            <tbody>
              {users.data?.items.map((user) => (
                <tr key={user.id}>
                  <td><strong>{user.displayName}</strong><br /><span>{user.email}</span></td>
                  <td>{user.isBlocked ? 'Blocked' : user.emailVerified ? 'Verified' : 'Unverified'}</td>
                  <td>{new Date(user.createdAt).toLocaleDateString()}</td>
                  <td className="actions">
                    <button onClick={() => block.mutate({ userId: user.id, blocked: !user.isBlocked })}>{user.isBlocked ? 'Unblock' : 'Block'}</button>
                    <button onClick={() => logout.mutate(user.id)}>Force logout</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </AsyncState>
    </section>
  );
}
