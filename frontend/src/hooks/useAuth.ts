import { useEffect, useState } from 'react';

export interface AuthUser {
  authenticated: true;
  userId: string;
  name: string;
  avatarUrl: string | null;
  alias: string | null;
  aliasUnavailable: boolean;
  isAdmin: boolean;
  provider: string | null;
}

interface UnauthenticatedState {
  authenticated: false;
}

type AuthApiResponse = AuthUser | UnauthenticatedState;

export interface AuthState {
  user: AuthUser | null;
  loading: boolean;
}

export function useAuth(): AuthState {
  const [state, setState] = useState<AuthState>({ user: null, loading: true });

  useEffect(() => {
    fetch('/api/auth/me', { credentials: 'same-origin' })
      .then(res => (res.ok ? (res.json() as Promise<AuthApiResponse>) : Promise.resolve({ authenticated: false } as UnauthenticatedState)))
      .then(data => setState({ user: data.authenticated ? data : null, loading: false }))
      .catch(() => setState({ user: null, loading: false }));
  }, []);

  return state;
}
