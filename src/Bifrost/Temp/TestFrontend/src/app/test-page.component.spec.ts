import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { TestPageComponent } from './test-page.component';

describe('Heimdall test pages', () => {
  let http: HttpTestingController;

  function setup(page: string) {
    TestBed.configureTestingModule({
      imports: [TestPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { page } } } }]
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(TestPageComponent);
  }

  afterEach(() => http.verify());

  it('loads the student identity with cookies through the gateway', () => {
    const fixture = setup('students');
    const request = http.expectOne('/api/students/me');
    expect(request.request.withCredentials).toBeTrue();
    request.flush({ userId: 'student-cookie-id', roles: ['Student'] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pre').textContent).toContain('student-cookie-id');
  });

  it('sends a request without a client-controlled student identity', () => {
    const fixture = setup('requests');
    http.expectOne('/api/students/requests').flush([]);
    fixture.componentInstance.id = 'request-123';
    fixture.componentInstance.message = 'Please help';
    fixture.componentInstance.createHelpRequest();
    const request = http.expectOne('/api/students/createhelprequest');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ id: 'request-123', message: 'Please help' });
    expect(request.request.withCredentials).toBeTrue();
    request.flush({ id: 'request-123' });
  });

  it('offers login when the teacher API requires authentication', () => {
    const fixture = setup('teachers');
    http.expectOne('/api/teachers/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('logge ind');
    expect(fixture.componentInstance.busy).toBeFalse();
  });

  it('explains a missing Teacher role separately from missing login', () => {
    const fixture = setup('teachers');
    http.expectOne('/api/teachers/me').flush(null, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Teacher');
  });
});
