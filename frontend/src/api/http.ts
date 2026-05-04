export const DEFAULT_TIMEOUT_MS = 20_000;

interface FetchWithTimeoutOptions extends RequestInit {
  timeoutMs?: number;
  retries?: number;
}

function shouldRetry(error: unknown): boolean {
  if (error instanceof DOMException && error.name === 'TimeoutError') return true;
  if (error instanceof TypeError) return true; // network/transient browser fetch failure
  return false;
}

export async function fetchWithTimeout(input: string, options: FetchWithTimeoutOptions = {}): Promise<Response> {
  const { timeoutMs = DEFAULT_TIMEOUT_MS, retries = 1, ...init } = options;

  let attempt = 0;
  while (true) {
    try {
      return await fetch(input, {
        ...init,
        signal: AbortSignal.timeout(timeoutMs),
      });
    } catch (error) {
      if (attempt >= retries || !shouldRetry(error)) throw error;
      attempt++;
    }
  }
}
