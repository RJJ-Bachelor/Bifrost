import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth-api';
import { Identity } from '../auth.model';

describe('AuthApi', () => {
  let service: AuthApi;
  let http: HttpTestingController;

  const teacher: Identity = {
    userId: 'teacher-123',
    name: 'Knud',
    roles: ['Teacher'],
  };

  const student: Identity = {
    userId: 'student-456',
    roles: ['Student'],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('fetches teacher identity from the teacher endpoint', async () => {
    const promise = firstValueFrom(service.fetchTeacherIdentity());

    const request = http.expectOne('/api/teachers/me');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush(teacher);

    expect(await promise).toEqual(teacher);
  });

  it('fetches student identity from the student endpoint', async () => {
    const promise = firstValueFrom(service.fetchStudentIdentity());

    const request = http.expectOne('/api/students/me');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush(student);

    expect(await promise).toEqual(student);
  });

  it('creates an identity transfer code from the student endpoint', async () => {
    const transfer = { code: 'ABCD2345', expiresAtUtc: '2026-11-17T15:00:00Z' };
    const promise = firstValueFrom(service.createIdentityTransferCode());

    const request = http.expectOne('/api/students/identity-transfer-codes');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({});
    expect(request.request.withCredentials).toBe(true);
    request.flush(transfer);

    expect(await promise).toEqual(transfer);
  });

  it('redeems an identity transfer code at the student endpoint', async () => {
    const promise = firstValueFrom(service.redeemIdentityTransferCode('ABCD2345'));

    const request = http.expectOne('/api/students/identity-transfer-codes/redeem');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ code: 'ABCD2345' });
    expect(request.request.withCredentials).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });

    await promise;
  });
});
