import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { HomeComponent } from './home.component';
import { IdentityPageComponent } from './identity-page.component';

describe('Glitnir access pages', () => {
  let http: HttpTestingController;

  function setup(kind = 'students') {
    TestBed.configureTestingModule({
      imports: [HomeComponent, IdentityPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { kind } } } }]
    });
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  it('keeps the home page free of data requests', () => {
    setup();
    TestBed.createComponent(HomeComponent).detectChanges();
    expect(http.match(() => true)).toEqual([]);
  });

  it('shows the student identity returned by the API and includes cookies', () => {
    setup();
    const fixture = TestBed.createComponent(IdentityPageComponent);
    const request = http.expectOne('/api/students/me');
    expect(request.request.withCredentials).toBeTrue();
    request.flush({ userId: 'cookie-student', roles: ['Student'] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.user-id').textContent).toContain('cookie-student');
    expect(fixture.componentInstance.status).toBe(200);
  });

  it('displays denied teacher access without redirecting automatically', () => {
    setup('teachers');
    const fixture = TestBed.createComponent(IdentityPageComponent);
    http.expectOne('/api/teachers/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(fixture.componentInstance.status).toBe(401);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Login');
    expect(fixture.componentInstance.identity).toBeNull();
  });

  it('shows Teacher data for an authenticated user', () => {
    setup('teachers');
    const fixture = TestBed.createComponent(IdentityPageComponent);
    http.expectOne('/api/teachers/me').flush({ userId: 'teacher-123', name: 'Knud', roles: ['Teacher'] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pre').textContent).toContain('Knud');
    expect(fixture.componentInstance.error).toBe('');
  });

  it('distinguishes a missing Teacher role from a missing login', () => {
    setup('teachers');
    const fixture = TestBed.createComponent(IdentityPageComponent);
    http.expectOne('/api/teachers/me').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(fixture.componentInstance.error).toContain('rollen Teacher');
    expect(fixture.componentInstance.busy).toBeFalse();
  });
});
