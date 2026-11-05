import { http, HttpResponse } from 'msw';
import { api, ApiError, setAccessToken } from '../api/client';
import { server } from '../test/server';

describe('api client', () => {
  afterEach(() => setAccessToken(null));

  it('refreshes once on 401 and retries the request with the new token', async () => {
    setAccessToken('expired');
    const seen: (string | null)[] = [];
    server.use(
      http.get('/api/students', ({ request }) => {
        const header = request.headers.get('Authorization');
        seen.push(header);
        return header === 'Bearer fresh'
          ? HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 })
          : HttpResponse.json({ title: 'Unauthorized' }, { status: 401 });
      }),
      http.post('/api/auth/refresh', () =>
        HttpResponse.json({ accessToken: 'fresh', expiresAt: '', user: { id: 'u', email: '', fullName: '', role: 'Tutor' } })),
    );

    const result = await api.get<{ totalCount: number }>('/api/students');

    expect(result.totalCount).toBe(0);
    expect(seen).toEqual(['Bearer expired', 'Bearer fresh']);
  });

  it('turns problem details into ApiError with field errors', async () => {
    server.use(http.post('/api/students', () => HttpResponse.json(
      { title: 'Validation failed', status: 400, errors: { YearLevel: ['Year must be 1-12'] } }, { status: 400 })));

    const error = await api.post('/api/students', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).field('yearLevel')).toBe('Year must be 1-12');
  });

  it('drops empty query values', async () => {
    let url = '';
    server.use(http.get('/api/lessons', ({ request }) => { url = request.url; return HttpResponse.json([]); }));
    await api.get('/api/lessons', { from: 'a', to: 'b', studentId: undefined });
    expect(new URL(url).search).toBe('?from=a&to=b');
  });
});
