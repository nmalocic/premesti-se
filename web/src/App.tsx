import { useEffect, useState } from 'react';

type State = 'loading' | 'healthy' | 'unavailable';

export function App() {
  const [state, setState] = useState<State>('loading');
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 5000);
    setState('loading');
    async function check() {
      try {
        const response = await fetch('/api/health', { signal: controller.signal, cache: 'no-store' });
        const health = await response.json();
        if (!response.ok || health.status !== 'Healthy' || health.checks?.database?.status !== 'Healthy') {
          throw new Error('Unavailable');
        }
        setState('healthy');
      } catch {
        if (!disposed) setState('unavailable');
      } finally {
        clearTimeout(timeout);
      }
    }
    let disposed = false;
    void check();
    return () => { disposed = true; clearTimeout(timeout); controller.abort(); };
  }, [attempt]);

  return (
    <main>
      <p className="eyebrow">Development starter</p>
      <h1>Premesti Se</h1>
      <p>A shared starting point for kindergarten transfers.</p>
      <section aria-labelledby="health-heading">
        <h2 id="health-heading">Stack connectivity</h2>
        <p role="status" aria-live="polite">
          {state === 'loading' && 'Checking API and database…'}
          {state === 'healthy' && 'API and VeloxDB are healthy.'}
          {state === 'unavailable' && 'API or database unavailable. Please try again.'}
        </p>
        <button onClick={() => setAttempt(a => a + 1)} disabled={state === 'loading'}>Check again</button>
      </section>
    </main>
  );
}
