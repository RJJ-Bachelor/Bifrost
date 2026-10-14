import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Identity } from '../auth.model';
import { AuthStore } from './auth-store';

describe('AuthStore', () => {
  let store: AuthStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(AuthStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('starts out unauthenticated', () => {
    expect(store.isAuthenticated()).toBe(false);
    expect(store.user()).toBeNull();
    expect(store.role()).toBeNull();
    expect(store.isTeacher()).toBe(false);
    expect(store.isStudent()).toBe(false);
  });

  it('loads a teacher identity from the teacher endpoint', async () => {
    const promise = store.loadIdentity('teacher');

    const request = http.expectOne('/api/teachers/me');
    request.flush({ userId: 't-1', name: 'Knud', roles: ['Teacher'] } satisfies Identity);
    await promise;

    expect(store.isAuthenticated()).toBe(true);
    expect(store.isTeacher()).toBe(true);
    expect(store.isStudent()).toBe(false);
    expect(store.user()?.name).toBe('Knud');
  });

  it('loads a student identity from the student endpoint', async () => {
    const promise = store.loadIdentity('student');

    const request = http.expectOne('/api/students/me');
    request.flush({ userId: 's-1', roles: ['Student'] } satisfies Identity);
    await promise;

    expect(store.isAuthenticated()).toBe(true);
    expect(store.isStudent()).toBe(true);
    expect(store.isTeacher()).toBe(false);
  });

  it('normalises role casing from the backend', async () => {
    const promise = store.loadIdentity('teacher');

    http.expectOne('/api/teachers/me').flush({
      userId: 't-1',
      roles: ['Teacher'],
    } satisfies Identity);
    await promise;

    expect(store.role()).toBe('teacher');
  });

  it('prioritises teacher over student when both roles are present', async () => {
    const promise = store.loadIdentity('teacher');

    http.expectOne('/api/teachers/me').flush({
      userId: 'u-1',
      roles: ['Student', 'Teacher'],
    } satisfies Identity);
    await promise;

    expect(store.role()).toBe('teacher');
  });

  it('resolves unknown roles to null', async () => {
    const promise = store.loadIdentity('teacher');

    http.expectOne('/api/teachers/me').flush({
      userId: 'u-1',
      roles: ['Admin'],
    } satisfies Identity);
    await promise;

    expect(store.role()).toBeNull();
    expect(store.isTeacher()).toBe(false);
    expect(store.isStudent()).toBe(false);
  });

  it('clears the identity when the request fails', async () => {
    const promise = store.loadIdentity('teacher');

    http
      .expectOne('/api/teachers/me')
      .flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await promise;

    expect(store.isAuthenticated()).toBe(false);
    expect(store.user()).toBeNull();
  });
});
