import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from './App';

const healthy = () =>
  new Response(
    JSON.stringify({
      status: 'Healthy',
      checks: { database: { status: 'Healthy' } },
    }),
  );

describe('stack health', () => {
  it('renders healthy after a successful API and database check', async () => {
    const fetch = vi.fn().mockResolvedValue(healthy());
    vi.stubGlobal('fetch', fetch);

    render(<App />);

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(
        'API and VeloxDB are healthy.',
      ),
    );
    expect(fetch).toHaveBeenCalledWith(
      '/api/health',
      expect.objectContaining({ cache: 'no-store' }),
    );
    expect(screen.getByRole('button', { name: 'Check again' })).toBeEnabled();
  });

  it.each([
    [
      'HTTP failure',
      () => Promise.resolve(new Response('{}', { status: 503 })),
    ],
    [
      'database failure',
      () =>
        Promise.resolve(
          new Response(
            JSON.stringify({
              status: 'Healthy',
              checks: { database: { status: 'Unhealthy' } },
            }),
          ),
        ),
    ],
    [
      'network failure',
      () => Promise.reject(new TypeError('Network unavailable')),
    ],
  ])(
    'reports unavailable on %s and recovers after retry',
    async (_name, failure) => {
      let completeRetry!: (response: Response) => void;
      const retry = new Promise<Response>((resolve) => {
        completeRetry = resolve;
      });
      const fetch = vi
        .fn()
        .mockImplementationOnce(failure)
        .mockReturnValueOnce(retry);
      vi.stubGlobal('fetch', fetch);

      render(<App />);

      await waitFor(() =>
        expect(screen.getByRole('status')).toHaveTextContent(
          'API or database unavailable.',
        ),
      );
      fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
      expect(screen.getByRole('status')).toHaveTextContent(
        'Checking API and database',
      );
      expect(
        screen.getByRole('button', { name: 'Check again' }),
      ).toBeDisabled();

      completeRetry(healthy());
      await waitFor(() =>
        expect(screen.getByRole('status')).toHaveTextContent(
          'API and VeloxDB are healthy.',
        ),
      );
      expect(fetch).toHaveBeenCalledTimes(2);
    },
  );
});
