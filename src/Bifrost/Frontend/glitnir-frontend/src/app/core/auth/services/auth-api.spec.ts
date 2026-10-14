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
});
