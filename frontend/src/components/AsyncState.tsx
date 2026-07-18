import { ReactNode } from 'react';

type Props = {
  loading?: boolean;
  error?: unknown;
  empty?: boolean;
  emptyText?: string;
  children: ReactNode;
};

export function AsyncState({ loading, error, empty, emptyText = 'Nothing to show yet.', children }: Props) {
  if (loading) return <div className="state">Loading...</div>;
  if (error) return <div className="state state-error">{error instanceof Error ? error.message : 'Something went wrong.'}</div>;
  if (empty) return <div className="state">{emptyText}</div>;
  return <>{children}</>;
}
